#!/usr/bin/env python3
"""The merge stage of the repair loop: a repair pull request merges once the judge approved and Copilot settled.

The job arms GitHub's auto-merge with its own token, `gh pr merge --auto --squash --delete-branch`, as #361 found:
GitHub accepts it from GITHUB_TOKEN, merges when the required checks `build`, `integration` and `judge-verdict` pass,
and shows `app/github-actions` as the merger. `--match-head-commit` ties the merge to the settled head, and the job
disarms the merge when the head moves while it waits, so no head that Copilot never settled on merges.

The guards, decided in #355 and #366, run first: the pull request is open with the label `repair`, its head is
`repair/*` or `factory/*`, its head is the SHA the Copilot stage settled on, and `judge-verdict` is success on that
head. The job waits for the judge when its check run is not complete yet.
A verdict other than success is one comment on the issue and no merge.

The loop bound: the job merges nothing on a pull request whose head branch is the branch of the red run that
started the repair. The run is the `Run:` line under `## Run` of the issue, which read.py wrote.

A merge by GITHUB_TOKEN closes no issue named by `Closes #n`, and `--delete-branch` deletes nothing on a deferred
merge, as #361 found. So the job waits for the merge, closes the issue with one comment that names the merge
commit, and deletes the head branch when `delete_branch_on_merge` left it. A push by GITHUB_TOKEN starts no
workflow run, so the job then starts `ci` on the base branch with workflow_dispatch, and the merge commit gets its
run on `main`. A wait that reaches the timeout disarms the auto-merge, so no later head merges unsettled. The job reads check runs with no
`checks` permission: the repo is public, and #361 bars `checks: write` on this job.

The kill switch REPAIR_ENABLED is read first, on every event. Any value other than `true` ends the step with the
exit code 0 and one line.

The merge timeout covers the wait for the judge and the wait for the merge. The defaults of the five waits of the job,
the rerun, the reader, the build, the Copilot stage and this one, add up to 18900 s, under GitHub's job limit of
21600 s. The required checks ran during the Copilot stage in the usual case, so the merge lands at once.

Environment: GH_TOKEN, GITHUB_REPOSITORY, REPAIR_ENABLED, REPAIR_MERGE_TIMEOUT_SECONDS (default 3600),
REPAIR_POLL_SECONDS (default 30).
Arguments: the number of the repair pull request, and the head SHA the Copilot stage settled on.
"""
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
from judge import gh  # noqa: E402

LABEL = "repair"
HEADS = ("repair/", "factory/")
CHECK = "judge-verdict"
MARKER = "<!-- repair-merge:{sha} -->"
FIELDS = "number,state,headRefName,headRefOid,baseRefName,body,labels,url,mergeCommit,mergedBy"


def read_pull_request(repo, number):
    return json.loads(gh("pr", "view", str(number), "-R", repo, "--json", FIELDS))


def closing_issue(body):
    match = re.search(r"^Closes #(\d+)\s*$", body or "", re.M)
    return int(match.group(1)) if match else None


def red_run_branch(repo, issue):
    """The head branch of the red run named under ## Run of the issue, or None when the issue names no run."""
    body = json.loads(gh("issue", "view", str(issue), "-R", repo, "--json", "body"))["body"] or ""
    match = re.search(rf"^Run: https://github\.com/{re.escape(repo)}/actions/runs/(\d+)\s*$", body, re.M)
    if not match:
        return None
    return json.loads(gh("api", f"repos/{repo}/actions/runs/{match.group(1)}"))["head_branch"]


def verdict(repo, sha):
    """The latest check run named judge-verdict on the commit: (status, conclusion), or None when the judge posted none."""
    answer = json.loads(gh("api", f"repos/{repo}/commits/{sha}/check-runs?check_name={CHECK}&filter=latest"))
    runs = answer.get("check_runs") or []
    return (runs[0]["status"], runs[0].get("conclusion")) if runs else None


def stop(repo, number, issue, sha, text):
    """One comment for the SHA, on the issue, or on the pull request when it closes no issue."""
    target = ("issue", str(issue)) if issue is not None else ("pr", str(number))
    comments = gh("api", f"repos/{repo}/issues/{target[1]}/comments", "--paginate", "--jq", ".[].body")
    marker = MARKER.format(sha=sha)
    if marker not in comments:
        gh(target[0], "comment", target[1], "-R", repo, "--body-file", "-", input_text=f"{marker}\n{text}")
    print(f"repair: no merge of #{number} on {sha[:7]}: {text.splitlines()[0]}")


def wait_for_judge(repo, sha, deadline, wait):
    while True:
        state = verdict(repo, sha)
        if state and state[0] == "completed":
            return state[1]
        if time.time() >= deadline:
            sys.exit(f"repair: {CHECK} on {sha[:7]} is {state[0] if state else 'absent'} after the timeout")
        print(f"repair: {CHECK} on {sha[:7]} is {state[0] if state else 'absent'}; waiting")
        time.sleep(wait)


def disarm(repo, number):
    """The auto-merge goes off. A failure of the disarm is one line, so the caller's own exit keeps its message."""
    try:
        gh("pr", "merge", str(number), "-R", repo, "--disable-auto")
    except SystemExit as error:
        print(f"repair: the disarm of pull request #{number} failed: {error}")
    else:
        print(f"repair: disarmed the auto-merge of pull request #{number}")


def arm_and_wait(repo, number, sha, deadline, wait):
    """Arms the auto-merge and waits for the merge. Any exit before the merge, a timeout, a closed pull request or a
    failed read, disarms the auto-merge first, so no armed merge outlives the step."""
    gh("pr", "merge", str(number), "-R", repo, "--auto", "--squash", "--delete-branch", "--match-head-commit", sha)
    print(f"repair: armed the auto-merge of pull request #{number} on {sha[:7]}")
    try:
        return wait_for_merge(repo, number, sha, deadline, wait)
    except SystemExit:
        disarm(repo, number)
        raise


def wait_for_merge(repo, number, sha, deadline, wait):
    """The merged pull request, or None once its head moved off the settled SHA, with the auto-merge disarmed."""
    while True:
        pr = read_pull_request(repo, number)
        if pr["state"] == "MERGED":
            return pr
        if pr["state"] != "OPEN":
            sys.exit(f"repair: pull request #{number} is {pr['state'].lower()} before the merge")
        if pr["headRefOid"] != sha:
            disarm(repo, number)
            print(f"repair: the head of pull request #{number} moved off {sha[:7]} while the auto-merge was armed; no merge")
            return None
        if time.time() >= deadline:
            sys.exit(f"repair: pull request #{number} is not merged after the timeout; "
                     f"`gh workflow run repair.yml -f pr={number}` arms the auto-merge again")
        time.sleep(wait)


def close_issue(repo, issue, pr):
    commit = (pr.get("mergeCommit") or {}).get("oid") or ""
    merger = (pr.get("mergedBy") or {}).get("login") or ""
    text = f"Merged as `{commit}` by pull request #{pr['number']}, merged by `{merger}`."
    if issue is None:
        print(f"repair: {text} The pull request closes no issue.")
        return
    state = json.loads(gh("issue", "view", str(issue), "-R", repo, "--json", "state"))["state"]
    if state != "OPEN":
        print(f"repair: issue #{issue} is {state.lower()} already. {text}")
        return
    gh("issue", "close", str(issue), "-R", repo, "--comment", text)
    print(f"repair: closed issue #{issue}. {text}")


def start_ci(repo, branch):
    """A run of ci on the base branch. A push by GITHUB_TOKEN starts no run, so the merge commit gets one here."""
    gh("workflow", "run", "ci.yml", "-R", repo, "--ref", branch)
    print(f"repair: started ci on {branch}")


def delete_branch(repo, branch):
    """The fallback for a repo with delete_branch_on_merge off: the head branch goes when it still exists."""
    refs = json.loads(gh("api", f"repos/{repo}/git/matching-refs/heads/{branch}"))
    if f"refs/heads/{branch}" not in [ref["ref"] for ref in refs]:
        return
    gh("api", "--method", "DELETE", f"repos/{repo}/git/refs/heads/{branch}")
    print(f"repair: deleted the branch {branch}")


def main():
    if os.environ.get("REPAIR_ENABLED") != "true":
        print("repair: REPAIR_ENABLED is not true; no merge")
        return
    repo = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    number = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else sys.exit("repair: give the number of a repair pull request")
    sha = sys.argv[2] if len(sys.argv) > 2 and re.fullmatch(r"[0-9a-f]{40}", sys.argv[2]) else sys.exit("repair: give the head SHA the Copilot stage settled on")

    pr = read_pull_request(repo, number)
    labels = [item["name"] for item in pr["labels"]]
    if pr["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: pull request #{number} is {pr['state'].lower()} with the labels {labels}; nothing to merge")
        return
    if not pr["headRefName"].startswith(HEADS):
        print(f"repair: the head {pr['headRefName']} of pull request #{number} is not repair/* or factory/*; nothing to merge")
        return
    if pr["headRefOid"] != sha:
        print(f"repair: the head of pull request #{number} is {pr['headRefOid'][:7]}, not the settled {sha[:7]}; nothing to merge")
        return
    issue = closing_issue(pr["body"])
    if issue is not None and red_run_branch(repo, issue) == pr["headRefName"]:
        stop(repo, number, issue, sha,
             f"The head `{pr['headRefName']}` of pull request #{number} is the branch of the red run that started this repair. "
             "The loop merges no branch of its own red run. The owner reads the pull request.")
        return

    wait = int(os.environ.get("REPAIR_POLL_SECONDS", "30"))
    deadline = time.time() + int(os.environ.get("REPAIR_MERGE_TIMEOUT_SECONDS", "3600"))
    conclusion = wait_for_judge(repo, sha, deadline, wait)
    if conclusion != "success":
        stop(repo, number, issue, sha,
             f"The check `{CHECK}` on `{sha}` of pull request #{number} is `{conclusion}`. No merge. The owner reads the judge's review.")
        return
    if read_pull_request(repo, number)["headRefOid"] != sha:
        print(f"repair: the head of pull request #{number} moved off {sha[:7]} while the judge ran; nothing to merge")
        return

    merged = arm_and_wait(repo, number, sha, deadline, wait)
    if merged is None:
        return
    if merged["headRefOid"] != sha:
        stop(repo, number, issue, sha,
             f"Pull request #{number} merged at `{merged['headRefOid']}`, not at the settled `{sha}`. The job closes no issue for it. The owner reads the pull request.")
        return
    close_issue(repo, issue, merged)
    start_ci(repo, merged["baseRefName"])
    delete_branch(repo, pr["headRefName"])


if __name__ == "__main__":
    main()
