#!/usr/bin/env python3
"""The Copilot stage of the repair loop: the step waits for the factory run that build.py started, and reads its
conclusion.

The factory run, started with `copilot=on`, owns the decision that Copilot is settled, as #509 decided. It requests
the Copilot review, answers each thread through FeedbackRun for three rounds at most, and ends `success` only when
every check is green, Copilot reviewed the head with no refusal, and no thread is unresolved. So this step asks one
question, "did the run pass?", and reads no review and no thread.

- `success`: the step reads the head SHA of the pull request once, and writes `copilot=settled` and `sha=<head>`.
- Any other conclusion: comments.py posts one comment for the head, behind a hidden marker that holds its SHA, on the
  issue the pull request closes, or on the pull request when it closes none. The comment links the run and names its
  conclusion. The step writes `copilot=unsettled` and `sha=<head>`. The run's log and job summary hold the reason.
- No `completed` within the timeout: the step exits 1 with one line.

A pull request that is closed or lacks the label `repair` ends the step with no output, and the run is never read,
even with FACTORY_RUN unset.

Environment: GH_TOKEN, GITHUB_REPOSITORY, FACTORY_RUN (the id of the factory run, the step output `run` of build.py,
required), REPAIR_COPILOT_TIMEOUT_SECONDS (default 7200), REPAIR_POLL_SECONDS (default 30).
Argument: the number of the repair pull request. The result goes to GITHUB_OUTPUT as `copilot`, `settled` or
`unsettled`, with the head SHA as `sha`, for the merge stage.
"""

import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shared"))
import closing  # noqa: E402
import comments  # noqa: E402
import runs  # noqa: E402
from github import GitHub  # noqa: E402
from repo import Repo  # noqa: E402

LABEL = "repair"


def output(**values):
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as out:
        for name, value in values.items():
            out.write(f"{name}={value}\n")


def stop(repo, number, issue, sha, text):
    """One comment for the SHA, on the issue, or on the pull request when it closes no issue."""
    target = issue if issue is not None else number
    comments.post_once(repo, target, f"<!-- repair-copilot:{sha} -->", text)
    print(f"repair: unsettled on {sha[:7]}: {text.splitlines()[0]}")


def main(repo=None):
    """`repo` is the Repo of GH_TOKEN, which a test hands in."""
    if repo is None:
        gh = GitHub.from_env()
        repo = Repo(gh, os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set"))
    number = (
        int(sys.argv[1])
        if len(sys.argv) > 1 and sys.argv[1].isdigit()
        else sys.exit("repair: give the number of a repair pull request")
    )
    pr = repo.pull_request(number)
    labels = [item["name"] for item in pr["labels"]]
    if pr["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: pull request #{number} is {pr['state'].lower()} with the labels {labels}; nothing to watch")
        return
    run_id = os.environ.get("FACTORY_RUN") or sys.exit("repair: FACTORY_RUN is not set; there is no run to wait on")
    wait = int(os.environ.get("REPAIR_POLL_SECONDS", "30"))
    deadline = time.time() + int(os.environ.get("REPAIR_COPILOT_TIMEOUT_SECONDS", "7200"))
    conclusion = runs.wait(repo, run_id, deadline, wait, name="the factory run")
    sha = repo.pull_request(number)["headRefOid"]
    if conclusion == "success":
        output(copilot="settled", sha=sha)
        print(f"repair: the factory run {runs.url(repo.name, run_id)} passed; settled on {sha[:7]}")
        return
    stop(
        repo,
        number,
        closing.issue(pr, repo.name),
        sha,
        f"The factory run {runs.url(repo.name, run_id)} of pull request #{number} ended `{conclusion}` on `{sha}`. "
        "The loop stops for this head. The owner reads the run: its log and job summary name the reason.",
    )
    output(copilot="unsettled", sha=sha)


if __name__ == "__main__":
    main()
