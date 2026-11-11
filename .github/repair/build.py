#!/usr/bin/env python3
"""The build stage of the repair loop: a repair issue becomes a pull request with the label repair.

The builder of the first loop is factory.yml, decided in #358. This job starts it with workflow_dispatch, the
issue number and `copilot=on`, under GITHUB_TOKEN. The factory runs at main, pushes factory/<id>, opens the pull
request with `Closes #<n>` under FACTORY_SELF_TOKEN, and posts `Opened <url>` on the issue. This job reads that
comment and sets the label repair on the pull request. The factory keeps its Redo bound of three, and posts
the last gate output on the issue when the bound is spent. With `copilot=on` it then requests a Copilot code
review and answers each thread through FeedbackRun, three rounds at most. copilot.py waits for that run.

A repair issue with the label factory is a fault of the factory itself, so the factory of main may be the broken
one. Decided in #399 and #403, this job builds it with the factory at its last green ref: the head SHA of the last
run of factory.yml on main that ended success. The dispatch API takes a branch or a tag and never a commit, so the
job creates the branch repair-base/<n> at that SHA, dispatches factory.yml at it with `base=main`, so the change
lands on the head of main, and deletes the branch when the step ends, passed or failed. The job reads the branch
first. It creates a missing branch at the SHA, and moves a branch of that name left by an earlier run to the SHA.
With no green run, the job comments on the issue and builds nothing.

Every repair lands on the head of main, so an issue whose traveler targets a pull request branch gets a comment and
no build. The traveler is the block under `## Run` of the issue, which read.py wrote and traveler.py reads.
The Managed Agents builder, #370, builds at the red SHA.

Environment: GH_TOKEN, GITHUB_REPOSITORY, REPAIR_BUILD_TIMEOUT_SECONDS (default 3600), and REPAIR_REF_TOKEN, the
token of the read, the create and the move of repair-base/<n>. With no REPAIR_REF_TOKEN, those calls use GH_TOKEN.
The deletion always uses GH_TOKEN.
Argument: the number of the repair issue. The pull request number goes to GITHUB_OUTPUT as `pr`, and the id of
the factory run as `run` when this job started one, for the Copilot stage.
"""

import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shared"))
import closing  # noqa: E402
import runs  # noqa: E402
import traveler  # noqa: E402
from github import GitHub, GitHubError  # noqa: E402
from repo import Repo  # noqa: E402

LABEL = "repair"
FACTORY_LABEL = "factory"
WORKFLOW = runs.WORKFLOW
BASE_BRANCH = "repair-base"
POLL = 30


def opened_pull_request(repo, issue):
    """The number of the pull request the factory posted on the issue as `Opened <url>`, or None.

    The factory posts under FACTORY_SELF_TOKEN, the owner's identity, so a comment by anyone else is no result.
    The pull request is open, on a `factory/` branch, and closes this issue, as GitHub reports it.
    """
    owner = repo.name.split("/")[0]
    for comment in issue.get("comments") or []:
        if (comment.get("author") or {}).get("login") != owner:
            continue
        match = re.search(
            rf"^Opened https://github\.com/{re.escape(repo.name)}/pull/(\d+)\s*$", comment.get("body") or "", re.M
        )
        if match and is_factory_pull_request(repo, int(match.group(1)), issue["number"]):
            return int(match.group(1))
    return None


def is_factory_pull_request(repo, pr, number):
    fields = repo.pull_request(pr)
    return (
        fields["state"] == "OPEN"
        and fields["headRefName"].startswith("factory/")
        and closing.issue(fields, repo.name) == number
    )


def green_sha(repo):
    """The head SHA of the last run of the factory on main that ended success, or None when no run did."""
    run = runs.find(repo, branch="main", status="success")
    return run["headSha"] if run else None


def repos():
    """The job's Repo, over GH_TOKEN, and the Repo of the read, the create and the move of repair-base/<n>: over
    REPAIR_REF_TOKEN when it is set and not empty, else the job's own."""
    gh = GitHub.from_env()
    name = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    job = Repo(gh, name)
    token = os.environ.get("REPAIR_REF_TOKEN")
    return job, Repo(GitHub(token), name) if token else job


def create_base_branch(app, branch, sha):
    """Points the branch at the SHA through the Repo `app`, and exits with GitHub's error text when a call fails.

    `app` holds REPAIR_REF_TOKEN when it is set: an installation token of the App dsf-builder with workflows write.
    GITHUB_TOKEN holds no workflows permission, and GitHub refuses it a ref at a commit whose workflow files differ
    from main (#403). It reads whether the branch exists first. A missing branch is created at the SHA. A branch of
    that name left by an earlier run is moved to the SHA. A failed read exits.
    """
    try:
        exists = app.has_branch(branch)
    except GitHubError as error:
        sys.exit(f"repair: the branch {branch} could not be read: {error.stderr.strip()[:500]}")
    verb, write = ("moved to", app.move_branch) if exists else ("created at", app.create_branch)
    try:
        write(branch, sha)
    except GitHubError as error:
        sys.exit(f"repair: the branch {branch} could not be {verb} {sha}: {error.stderr.strip()[:500]}")
    print(f"repair: the branch {branch} is at the green SHA {sha}")


def delete_base_branch(job, branch):
    """Deletes the branch through the job's Repo. A failure is a log line, so it hides no build error.

    The deletion runs after the wait for the factory run, up to REPAIR_BUILD_TIMEOUT_SECONDS later. An installation
    token lives one hour, so by then REPAIR_REF_TOKEN can be expired. GH_TOKEN lives for the job. A deletion adds no
    workflow file, so it needs no workflows permission.
    """
    try:
        job.delete_branch(branch)
    except GitHubError as error:
        print(f"repair: the deletion of the branch {branch} failed: {error.stderr.strip()[:500]}")
    else:
        print(f"repair: deleted the branch {branch}")


def label(repo, pr):
    repo.label_pull_request(pr, LABEL)
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as output:
        output.write(f"pr={pr}\n")
    print(f"repair: pull request #{pr} has the label {LABEL}")


def wait_for_pull_request(repo, number, run, timeout):
    """The pull request the factory opens, read from the issue's comments while its run is in progress."""

    def opened():
        pr = opened_pull_request(repo, repo.issue(number))
        if pr is not None:
            return pr, ""
        current = repo.run_status(run["databaseId"])
        if current["status"] == "completed":
            pr = opened_pull_request(repo, repo.issue(number))
            if pr is not None:
                return pr, ""
            sys.exit(
                f"repair: the factory run {run['url']} ended {current['conclusion']} "
                f"with no pull request on issue #{number}"
            )
        return None, f"the factory run {run['url']} has opened no pull request on issue #{number}"

    return runs.poll(opened, time.time() + timeout, POLL)


def build(repo, number, timeout, ref=None):
    """Dispatches the factory, waits for the pull request it opens, and labels it."""
    inputs = {"base": "main", "copilot": "on"} if ref else {"copilot": "on"}
    run = runs.dispatch(repo, number, ref=ref, inputs=inputs)
    print(f"repair: started the factory: {run['url']}")
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as output:
        output.write(f"run={run['databaseId']}\n")
    pr = wait_for_pull_request(repo, number, run, timeout)
    label(repo, pr)


def main(job=None, app=None):
    """`job` and `app` are the Repo objects of repos(), which a test hands in."""
    if job is None:
        job, app = repos()
    app = app if app is not None else job
    number = (
        int(sys.argv[1])
        if len(sys.argv) > 1 and sys.argv[1].isdigit()
        else sys.exit("repair: give the number of a repair issue")
    )
    issue = job.issue(number)
    labels = [item["name"] for item in issue.get("labels") or []]
    if issue["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: issue #{number} is {issue['state'].lower()} with the labels {labels}; nothing to build")
        return
    if (pr := opened_pull_request(job, issue)) is not None:
        print(f"repair: issue #{number} holds pull request #{pr} already")
        label(job, pr)
        return
    ticket = traveler.parse(issue["body"])
    if ticket is not None and (target := ticket.pull_request) is not None:
        job.post_comment(
            number,
            f"Every repair lands on the head of `main`, and the fault is on the branch of pull request #{target}. "
            "No build. A builder at the red SHA is the Managed Agents builder, #370.",
        )
        print(f"repair: issue #{number} targets pull request #{target}; no build at main")
        return

    timeout = int(os.environ.get("REPAIR_BUILD_TIMEOUT_SECONDS", "3600"))
    if FACTORY_LABEL not in labels:
        build(job, number, timeout)
        return
    sha = green_sha(job)
    if sha is None:
        job.post_comment(
            number,
            f"No run of `{WORKFLOW}` on `main` ended `success`, so the factory has no green ref to build this "
            "repair at. No build.",
        )
        print(f"repair: no green run of {WORKFLOW} on main; no build of issue #{number}")
        return
    branch = f"{BASE_BRANCH}/{number}"
    try:
        create_base_branch(app, branch, sha)
        build(job, number, timeout, branch)
    finally:
        delete_base_branch(job, branch)


if __name__ == "__main__":
    main()
