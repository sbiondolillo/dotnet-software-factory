#!/usr/bin/env python3
"""The build stage of the repair loop: a repair issue becomes a pull request with the label repair.

The builder of the first loop is factory.yml, decided in #358. This job starts it with workflow_dispatch and
the issue number, under GITHUB_TOKEN. The factory runs at main, pushes factory/<id>, opens the pull request
with `Closes #<n>` under FACTORY_SELF_TOKEN, and posts `Opened <url>` on the issue. This job reads that
comment and sets the label repair on the pull request. The factory keeps its Redo bound of three, and posts
the last gate output on the issue when the bound is spent.

The factory builds at main, so an issue whose target is a pull request branch gets a comment and no build.
The Managed Agents builder, #370, builds at the red SHA.

Environment: GH_TOKEN, GITHUB_REPOSITORY, REPAIR_BUILD_TIMEOUT_SECONDS (default 3600).
Argument: the number of the repair issue.
"""
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
from judge import gh  # noqa: E402

LABEL = "repair"
WORKFLOW = "factory.yml"


def read_issue(repo, number):
    return json.loads(gh("issue", "view", str(number), "-R", repo, "--json", "number,state,body,labels,comments"))


def target_pull_request(body):
    """The pull request number of the issue's target branch under ## Run, or None for a branch from main."""
    match = re.search(r"^Target branch: .*\(pull request #(\d+)\)", body or "", re.M)
    return int(match.group(1)) if match else None


def opened_pull_request(repo, issue):
    """The number of the pull request the factory posted on the issue as `Opened <url>`, or None."""
    for comment in issue.get("comments") or []:
        match = re.search(rf"^Opened https://github\.com/{re.escape(repo)}/pull/(\d+)\s*$", comment.get("body") or "", re.M)
        if match:
            return int(match.group(1))
    return None


def factory_runs(repo):
    runs = json.loads(gh("run", "list", "-R", repo, "--workflow", WORKFLOW, "--event", "workflow_dispatch", "--limit", "30",
                         "--json", "databaseId,status,conclusion,url"))
    return {run["databaseId"]: run for run in runs}


def dispatch(repo, number, timeout):
    """Starts the factory on the issue, and gives back its run once the list holds it."""
    before = factory_runs(repo)
    gh("workflow", "run", WORKFLOW, "-R", repo, "-f", f"issue={number}")
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(10)
        new = [run for run_id, run in factory_runs(repo).items() if run_id not in before]
        if new:
            return new[0]
    sys.exit(f"repair: the dispatch of {WORKFLOW} for issue #{number} shows no run after {timeout} s")


def label(repo, pr):
    gh("pr", "edit", str(pr), "-R", repo, "--add-label", LABEL)
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as output:
        output.write(f"pr={pr}\n")
    print(f"repair: pull request #{pr} has the label {LABEL}")


def wait_for_pull_request(repo, number, run, timeout):
    """The pull request the factory opens, read from the issue's comments while its run is in progress."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        pr = opened_pull_request(repo, read_issue(repo, number))
        if pr is not None:
            return pr
        current = json.loads(gh("run", "view", str(run["databaseId"]), "-R", repo, "--json", "status,conclusion,url"))
        if current["status"] == "completed":
            sys.exit(f"repair: the factory run {current['url']} ended {current['conclusion']} with no pull request on issue #{number}")
        time.sleep(30)
    sys.exit(f"repair: the factory run {run['url']} opened no pull request on issue #{number} within {timeout} s")


def main():
    repo = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    number = int(sys.argv[1])
    issue = read_issue(repo, number)
    labels = [item["name"] for item in issue.get("labels") or []]
    if issue["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: issue #{number} is {issue['state'].lower()} with the labels {labels}; nothing to build")
        return
    if (pr := opened_pull_request(repo, issue)) is not None:
        print(f"repair: issue #{number} holds pull request #{pr} already")
        label(repo, pr)
        return
    if (target := target_pull_request(issue["body"])) is not None:
        gh("issue", "comment", str(number), "-R", repo, "--body",
           f"The factory builds at `main`, and the fault is on the branch of pull request #{target}. No build. "
           "A builder at the red SHA is the Managed Agents builder, #370.")
        print(f"repair: issue #{number} targets pull request #{target}; no build at main")
        return

    timeout = int(os.environ.get("REPAIR_BUILD_TIMEOUT_SECONDS", "3600"))
    run = dispatch(repo, number, 180)
    print(f"repair: started the factory: {run['url']}")
    pr = wait_for_pull_request(repo, number, run, timeout)
    label(repo, pr)


if __name__ == "__main__":
    main()
