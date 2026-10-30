#!/usr/bin/env python3
"""The Copilot stage of the repair loop: a repair pull request gets a Copilot review, and every thread an answer.

The job requests `copilot-pull-request-reviewer` on the pull request when its head holds no review by Copilot and
no request is pending, as ConsoleCommands.WatchAsync does, and polls as PullRequestWatch does. Settled is a review
by Copilot on the head, no refusal on the head, and zero unresolved threads.

The builder of the first loop is factory.yml, which build.py starts with `copilot=on`, decided in #358. So the
factory requests the review as well, and answers each thread through FeedbackRun within its run, three rounds at
most, under FACTORY_SELF_TOKEN. This job waits while that run is in progress, and counts the rounds by the head: each
push after the first head is one round. A refusal by Copilot, a thread still unresolved once the run ended, or a
fourth round, is a comment on the issue and a stop for the head SHA. The Managed Agents
builder, #370, answers each thread in a feedback session, and this job then posts the replies with its own token.

Environment: GH_TOKEN, GITHUB_REPOSITORY, FACTORY_RUN (the id of the builder's run, optional),
REPAIR_COPILOT_TIMEOUT_SECONDS (default 7200), REPAIR_POLL_SECONDS (default 30).
Argument: the number of the repair pull request. The result goes to GITHUB_OUTPUT as `copilot`, one of `settled`,
`refused` or `unanswered`, with the head SHA as `sha`, for the merge stage.
"""

import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
from judge import gh  # noqa: E402

LABEL = "repair"
REVIEWER = "copilot-pull-request-reviewer"
REFUSAL = "Copilot was unable to review this pull request"
MARKER = "<!-- repair-copilot:{sha} -->"
WORKFLOW = "factory.yml"
ROUNDS = 3
QUERY = """query($o: String!, $r: String!, $n: Int!) { repository(owner: $o, name: $r) { pullRequest(number: $n) {
  id state headRefOid body labels(first: 50) { nodes { name } }
  reviewRequests(first: 50) { nodes { requestedReviewer { ... on Bot { login } ... on User { login } } } }
  reviews(first: 100) { nodes { state body author { login } commit { oid } } }
  reviewThreads(first: 100) { nodes { id isResolved } } } } }"""
REQUEST = (
    "mutation($id: ID!, $logins: [String!]) "
    "{ requestReviewsByLogin(input: {pullRequestId: $id, botLogins: $logins}) { pullRequest { id } } }"
)


def read_pull_request(repo, number):
    owner, name = repo.split("/")
    answer = json.loads(
        gh("api", "graphql", "-f", f"query={QUERY}", "-F", f"o={owner}", "-F", f"r={name}", "-F", f"n={number}")
    )
    return answer["data"]["repository"]["pullRequest"]


def reviews_on(pr, sha):
    """The reviews by Copilot on a commit, in GitHub's order, oldest first."""
    return [
        r
        for r in pr["reviews"]["nodes"]
        if (r.get("author") or {}).get("login") == REVIEWER and ((r.get("commit") or {}).get("oid") or "") == sha
    ]


def refusal(pr, sha):
    """The body of the last review by Copilot on the commit when it is a refusal, or None, as Review.Refusal."""
    last = reviews_on(pr, sha)[-1:] or [None]
    if last[0] and last[0].get("state") == "COMMENTED" and (last[0].get("body") or "").startswith(REFUSAL):
        return last[0]["body"]
    return None


def requested(pr):
    return any((n.get("requestedReviewer") or {}).get("login") == REVIEWER for n in pr["reviewRequests"]["nodes"])


def unresolved(pr):
    return [t["id"] for t in pr["reviewThreads"]["nodes"] if not t["isResolved"]]


def request_review(pr):
    gh("api", "graphql", "-f", f"query={REQUEST}", "-F", f"id={pr['id']}", "-f", f"logins[]={REVIEWER}")


def closing_issue(body):
    match = re.search(r"^Closes #(\d+)\s*$", body or "", re.M)
    return int(match.group(1)) if match else None


def builder_running(repo):
    """Whether the builder's run is in progress: the run FACTORY_RUN names, or any run of factory.yml by
    workflow_dispatch otherwise. The owner's label runs do not count."""
    run_id = os.environ.get("FACTORY_RUN")
    if run_id:
        return json.loads(gh("run", "view", run_id, "-R", repo, "--json", "status"))["status"] != "completed"
    runs = json.loads(
        gh(
            "run",
            "list",
            "-R",
            repo,
            "--workflow",
            WORKFLOW,
            "--event",
            "workflow_dispatch",
            "--limit",
            "30",
            "--json",
            "status",
        )
    )
    return any(run["status"] != "completed" for run in runs)


def output(**values):
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as out:
        for name, value in values.items():
            out.write(f"{name}={value}\n")


def stop(repo, number, issue, sha, result, text):
    """One comment on the issue for the SHA, then the result for the merge stage. The comment goes on the pull
    request when it closes no issue."""
    target = ("issue", str(issue)) if issue is not None else ("pr", str(number))
    comments = gh("api", f"repos/{repo}/issues/{target[1]}/comments", "--paginate", "--jq", ".[].body")
    marker = MARKER.format(sha=sha)
    if marker not in comments:
        gh(target[0], "comment", target[1], "-R", repo, "--body-file", "-", input_text=f"{marker}\n{text}")
    output(copilot=result, sha=sha)
    print(f"repair: {result} on {sha[:7]}: {text.splitlines()[0]}")


def main():
    repo = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    number = (
        int(sys.argv[1])
        if len(sys.argv) > 1 and sys.argv[1].isdigit()
        else sys.exit("repair: give the number of a repair pull request")
    )
    pr = read_pull_request(repo, number)
    labels = [item["name"] for item in pr["labels"]["nodes"]]
    if pr["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: pull request #{number} is {pr['state'].lower()} with the labels {labels}; nothing to watch")
        return
    issue = closing_issue(pr["body"])
    wait = int(os.environ.get("REPAIR_POLL_SECONDS", "30"))
    deadline = time.time() + int(os.environ.get("REPAIR_COPILOT_TIMEOUT_SECONDS", "7200"))
    asked = set()
    heads = [pr["headRefOid"]]
    while True:
        sha = pr["headRefOid"]
        if sha != heads[-1]:
            heads.append(sha)
            if len(heads) > ROUNDS + 1:
                stop(
                    repo,
                    number,
                    issue,
                    sha,
                    "unanswered",
                    f"Pull request #{number} took {ROUNDS} rounds of answers to Copilot, and `{sha}` starts a fourth. "
                    f"The loop stops for this SHA. The owner reads the threads.",
                )
                return
        if (body := refusal(pr, sha)) is not None:
            stop(
                repo,
                number,
                issue,
                sha,
                "refused",
                f"Copilot refused to review pull request #{number} on `{sha}`. The loop stops for this SHA.\n\n"
                f"> {body.splitlines()[0]}",
            )
            return
        threads = unresolved(pr)
        if reviews_on(pr, sha):
            if not threads:
                output(copilot="settled", sha=sha)
                print(f"repair: Copilot reviewed {sha[:7]} and every thread is resolved")
                return
            if not builder_running(repo):
                stop(
                    repo,
                    number,
                    issue,
                    sha,
                    "unanswered",
                    f"Copilot left {len(threads)} unresolved thread{'s' if len(threads) != 1 else ''} "
                    f"on `{sha}` of pull request #{number}, "
                    f"and the builder's run ended. The loop stops for this SHA. The owner reads the threads.",
                )
                return
        elif sha not in asked and not requested(pr):
            request_review(pr)
            asked.add(sha)
            print(f"repair: requested a review by {REVIEWER} on {sha[:7]}")
        if time.time() >= deadline:
            sys.exit(
                f"repair: pull request #{number} is not settled on {sha[:7]} after the timeout: "
                f"{len(reviews_on(pr, sha))} reviews by Copilot on the head, {len(threads)} unresolved threads"
            )
        time.sleep(wait)
        pr = read_pull_request(repo, number)


if __name__ == "__main__":
    main()
