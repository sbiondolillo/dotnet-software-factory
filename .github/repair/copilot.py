#!/usr/bin/env python3
"""The Copilot stage of the repair loop: the step waits for the factory run that build.py started, and reads its
conclusion.

The factory run, started with `copilot=on`, owns the decision that Copilot is settled, as #509 decided. It requests
the Copilot review, answers each thread through FeedbackRun for three rounds at most, and ends `success` only when
every check is green, Copilot reviewed the head with no refusal, and no thread is unresolved. So this step asks one
question, "did the run pass?", and reads no review and no thread.

- `success`: the step reads the head SHA of the pull request once, and writes `copilot=settled` and `sha=<head>`.
- Any other conclusion: one comment under the marker `<!-- repair-copilot:<sha> -->`, on the issue the pull
  request closes, or on the pull request when it closes none. The comment links the run and names its conclusion.
  The step writes `copilot=unsettled` and `sha=<head>`. The run's log and job summary hold the reason.
- No `completed` within the timeout: the step exits 1 with one line.

A pull request that is closed or lacks the label `repair` ends the step with no output, and the run is never read,
even with FACTORY_RUN unset.

Environment: GH_TOKEN, GITHUB_REPOSITORY, FACTORY_RUN (the id of the factory run, the step output `run` of build.py,
required), REPAIR_COPILOT_TIMEOUT_SECONDS (default 7200), REPAIR_POLL_SECONDS (default 30).
Argument: the number of the repair pull request. The result goes to GITHUB_OUTPUT as `copilot`, `settled` or
`unsettled`, with the head SHA as `sha`, for the merge stage.
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shared"))
import traveler  # noqa: E402
from github import GitHub  # noqa: E402

LABEL = "repair"
MARKER = "<!-- repair-copilot:{sha} -->"
FIELDS = "state,labels,body,headRefOid"


def read_pull_request(gh, repo, number):
    return json.loads(gh.run("pr", "view", str(number), "-R", repo, "--json", FIELDS))


def run_url(repo, run_id):
    return f"https://github.com/{repo}/actions/runs/{run_id}"


def wait_for_run(gh, repo, run_id, deadline, wait):
    """The conclusion of the run once its status is `completed`. The step exits 1 when the deadline passes first."""
    while True:
        run = json.loads(gh.run("run", "view", run_id, "-R", repo, "--json", "status,conclusion"))
        if run["status"] == "completed":
            return run["conclusion"]
        if time.time() >= deadline:
            sys.exit(f"repair: the factory run {run_url(repo, run_id)} is {run['status']} after the timeout")
        print(f"repair: the factory run {run_url(repo, run_id)} is {run['status']}; waiting")
        time.sleep(max(0, min(wait, deadline - time.time())))


def output(**values):
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as out:
        for name, value in values.items():
            out.write(f"{name}={value}\n")


def stop(gh, repo, number, issue, sha, text):
    """One comment for the SHA, on the issue, or on the pull request when it closes no issue."""
    target = ("issue", str(issue)) if issue is not None else ("pr", str(number))
    comments = gh.run("api", f"repos/{repo}/issues/{target[1]}/comments", "--paginate", "--jq", ".[].body")
    marker = MARKER.format(sha=sha)
    if marker not in comments:
        gh.run(target[0], "comment", target[1], "-R", repo, "--body-file", "-", input_text=f"{marker}\n{text}")
    print(f"repair: unsettled on {sha[:7]}: {text.splitlines()[0]}")


def main(gh=None):
    gh = gh if gh is not None else GitHub.from_env()
    repo = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    number = (
        int(sys.argv[1])
        if len(sys.argv) > 1 and sys.argv[1].isdigit()
        else sys.exit("repair: give the number of a repair pull request")
    )
    pr = read_pull_request(gh, repo, number)
    labels = [item["name"] for item in pr["labels"]]
    if pr["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: pull request #{number} is {pr['state'].lower()} with the labels {labels}; nothing to watch")
        return
    run_id = os.environ.get("FACTORY_RUN") or sys.exit("repair: FACTORY_RUN is not set; there is no run to wait on")
    wait = int(os.environ.get("REPAIR_POLL_SECONDS", "30"))
    deadline = time.time() + int(os.environ.get("REPAIR_COPILOT_TIMEOUT_SECONDS", "7200"))
    conclusion = wait_for_run(gh, repo, run_id, deadline, wait)
    sha = read_pull_request(gh, repo, number)["headRefOid"]
    if conclusion == "success":
        output(copilot="settled", sha=sha)
        print(f"repair: the factory run {run_url(repo, run_id)} passed; settled on {sha[:7]}")
        return
    stop(
        gh,
        repo,
        number,
        traveler.closing_issue(pr["body"]),
        sha,
        f"The factory run {run_url(repo, run_id)} of pull request #{number} ended `{conclusion}` on `{sha}`. "
        "The loop stops for this head. The owner reads the run: its log and job summary name the reason.",
    )
    output(copilot="unsettled", sha=sha)


if __name__ == "__main__":
    main()
