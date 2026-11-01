#!/usr/bin/env python3
"""The build stage of the repair loop: a repair issue becomes a pull request with the label repair.

The builder of the first loop is factory.yml, decided in #358. This job starts it with workflow_dispatch, the
issue number and `copilot=on`, under GITHUB_TOKEN. The factory runs at main, pushes factory/<id>, opens the pull
request with `Closes #<n>` under FACTORY_SELF_TOKEN, and posts `Opened <url>` on the issue. This job reads that
comment and sets the label repair on the pull request. The factory keeps its Redo bound of three, and posts
the last gate output on the issue when the bound is spent. With `copilot=on` it then requests a Copilot code
review and answers each thread through FeedbackRun, three rounds at most, which copilot.py watches.

A repair issue with the label factory is a fault of the factory itself, so the factory of main may be the broken
one. Decided in #399 and #403, this job builds it with the factory at its last green ref: the head SHA of the last
run of factory.yml on main that ended success. The dispatch API takes a branch or a tag and never a commit, so the
job creates the branch repair-base/<n> at that SHA, dispatches factory.yml at it with `base=main`, so the change
lands on the head of main, and deletes the branch when the step ends, passed or failed. The job reads the branch
first. It creates a missing branch at the SHA, and moves a branch of that name left by an earlier run to the SHA.
With no green run, the job comments on the issue and builds nothing.

Every repair lands on the head of main, so an issue whose target is a pull request branch gets a comment and no
build.
The Managed Agents builder, #370, builds at the red SHA.

Environment: GH_TOKEN, GITHUB_REPOSITORY, REPAIR_BUILD_TIMEOUT_SECONDS (default 3600), and REPAIR_REF_TOKEN, the
token of the read, the create and the move of repair-base/<n>. With no REPAIR_REF_TOKEN, those calls use GH_TOKEN.
The deletion always uses GH_TOKEN.
Argument: the number of the repair issue. The pull request number goes to GITHUB_OUTPUT as `pr`, and the id of
the factory run as `run` when this job started one, for the Copilot stage.
"""

import json
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
from judge import gh  # noqa: E402

LABEL = "repair"
FACTORY_LABEL = "factory"
WORKFLOW = "factory.yml"
BASE_BRANCH = "repair-base"


def read_issue(repo, number):
    return json.loads(gh("issue", "view", str(number), "-R", repo, "--json", "number,state,body,labels,comments"))


def target_pull_request(body):
    """The pull request number of the issue's target branch under ## Run, or None for a branch from main."""
    match = re.search(r"^Target branch: .*\(pull request #(\d+)\)", body or "", re.M)
    return int(match.group(1)) if match else None


def opened_pull_request(repo, issue):
    """The number of the pull request the factory posted on the issue as `Opened <url>`, or None.

    The factory posts under FACTORY_SELF_TOKEN, the owner's identity, so a comment by anyone else is no result.
    The pull request is open, on a `factory/` branch, and holds `Closes #<n>` for this issue.
    """
    owner = repo.split("/")[0]
    for comment in issue.get("comments") or []:
        if (comment.get("author") or {}).get("login") != owner:
            continue
        match = re.search(
            rf"^Opened https://github\.com/{re.escape(repo)}/pull/(\d+)\s*$", comment.get("body") or "", re.M
        )
        if match and is_factory_pull_request(repo, int(match.group(1)), issue["number"]):
            return int(match.group(1))
    return None


def is_factory_pull_request(repo, pr, number):
    fields = json.loads(gh("pr", "view", str(pr), "-R", repo, "--json", "state,headRefName,body"))
    return (
        fields["state"] == "OPEN"
        and fields["headRefName"].startswith("factory/")
        and re.search(rf"^Closes #{number}\s*$", fields["body"] or "", re.M) is not None
    )


def factory_runs(repo):
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
            "databaseId,status,conclusion,url",
        )
    )
    return {run["databaseId"]: run for run in runs}


def green_sha(repo):
    """The head SHA of the last run of the factory on main that ended success, or None when no run did."""
    runs = json.loads(
        gh(
            "run",
            "list",
            "-R",
            repo,
            "--workflow",
            WORKFLOW,
            "--status",
            "success",
            "--branch",
            "main",
            "--limit",
            "1",
            "--json",
            "headSha",
        )
    )
    return runs[0]["headSha"] if runs else None


def ref_env():
    """The environment of the create and the move: GH_TOKEN is REPAIR_REF_TOKEN when it is set and not empty.

    Else it is the environment of the process, unchanged.
    """
    token = os.environ.get("REPAIR_REF_TOKEN")
    return {**os.environ, "GH_TOKEN": token} if token else dict(os.environ)


def create_base_branch(repo, branch, sha):
    """Points the branch at the SHA, and exits with GitHub's error text when a call fails.

    The calls use REPAIR_REF_TOKEN when it is set: an installation token of the App dsf-builder with workflows
    write. GITHUB_TOKEN holds no workflows permission, and GitHub refuses it a ref at a commit whose workflow files
    differ from main (#403). It reads the ref first. A 404 means no branch of that name, so a POST creates it at the
    SHA. A branch of that name left by an earlier run gets a forced PATCH that moves it to the SHA. Any other error of
    the read exits.
    """
    env = ref_env()
    found = subprocess.run(
        ["gh", "api", f"repos/{repo}/git/ref/heads/{branch}"], capture_output=True, text=True, env=env
    )
    if found.returncode == 0:
        verb = "moved to"
        call = ["-X", "PATCH", f"repos/{repo}/git/refs/heads/{branch}", "-f", f"sha={sha}", "-F", "force=true"]
    elif "(HTTP 404)" in found.stderr:
        verb = "created at"
        call = ["-X", "POST", f"repos/{repo}/git/refs", "-f", f"ref=refs/heads/{branch}", "-f", f"sha={sha}"]
    else:
        sys.exit(f"repair: the branch {branch} could not be read: {found.stderr.strip()[:500]}")
    sent = subprocess.run(["gh", "api", *call], capture_output=True, text=True, env=env)
    if sent.returncode != 0:
        sys.exit(f"repair: the branch {branch} could not be {verb} {sha}: {sent.stderr.strip()[:500]}")
    print(f"repair: the branch {branch} is at the green SHA {sha}")


def delete_base_branch(repo, branch):
    """Deletes the branch with GH_TOKEN. A failure is a log line, so it hides no build error.

    The deletion runs after the wait for the factory run, up to REPAIR_BUILD_TIMEOUT_SECONDS later. An installation
    token lives one hour, so by then REPAIR_REF_TOKEN can be expired. GH_TOKEN lives for the job. A deletion adds no
    workflow file, so it needs no workflows permission.
    """
    deleted = subprocess.run(
        ["gh", "api", "-X", "DELETE", f"repos/{repo}/git/refs/heads/{branch}"], capture_output=True, text=True
    )
    if deleted.returncode == 0:
        print(f"repair: deleted the branch {branch}")
    else:
        print(f"repair: the deletion of the branch {branch} failed: {deleted.stderr.strip()[:500]}")


def dispatch(repo, number, timeout, ref=None):
    """Starts the factory on the issue, and gives back its run once the list holds it.

    With a ref, the factory of that ref builds on the head of main, by `base=main`. With none, it runs at main.
    """
    before = factory_runs(repo)
    at = ["--ref", ref, "-f", "base=main"] if ref else []
    gh("workflow", "run", WORKFLOW, "-R", repo, *at, "-f", f"issue={number}", "-f", "copilot=on")
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
            pr = opened_pull_request(repo, read_issue(repo, number))
            if pr is not None:
                return pr
            sys.exit(
                f"repair: the factory run {current['url']} ended {current['conclusion']} "
                f"with no pull request on issue #{number}"
            )
        time.sleep(30)
    sys.exit(f"repair: the factory run {run['url']} opened no pull request on issue #{number} within {timeout} s")


def build(repo, number, timeout, ref=None):
    """Dispatches the factory, waits for the pull request it opens, and labels it."""
    run = dispatch(repo, number, 180, ref)
    print(f"repair: started the factory: {run['url']}")
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as output:
        output.write(f"run={run['databaseId']}\n")
    pr = wait_for_pull_request(repo, number, run, timeout)
    label(repo, pr)


def main():
    repo = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    number = (
        int(sys.argv[1])
        if len(sys.argv) > 1 and sys.argv[1].isdigit()
        else sys.exit("repair: give the number of a repair issue")
    )
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
        gh(
            "issue",
            "comment",
            str(number),
            "-R",
            repo,
            "--body",
            f"Every repair lands on the head of `main`, and the fault is on the branch of pull request #{target}. "
            "No build. A builder at the red SHA is the Managed Agents builder, #370.",
        )
        print(f"repair: issue #{number} targets pull request #{target}; no build at main")
        return

    timeout = int(os.environ.get("REPAIR_BUILD_TIMEOUT_SECONDS", "3600"))
    if FACTORY_LABEL not in labels:
        build(repo, number, timeout)
        return
    sha = green_sha(repo)
    if sha is None:
        gh(
            "issue",
            "comment",
            str(number),
            "-R",
            repo,
            "--body",
            f"No run of `{WORKFLOW}` on `main` ended `success`, so the factory has no green ref to build this "
            "repair at. No build.",
        )
        print(f"repair: no green run of {WORKFLOW} on main; no build of issue #{number}")
        return
    branch = f"{BASE_BRANCH}/{number}"
    try:
        create_base_branch(repo, branch, sha)
        build(repo, number, timeout, branch)
    finally:
        delete_base_branch(repo, branch)


if __name__ == "__main__":
    main()
