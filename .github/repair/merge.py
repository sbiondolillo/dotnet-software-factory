#!/usr/bin/env python3
"""The merge stage of the repair loop: a repair pull request merges once the judge approved and Copilot settled.

The job arms GitHub's auto-merge with its own token, `gh pr merge --auto --squash --delete-branch`, as #361 found:
GitHub accepts it from GITHUB_TOKEN, merges when the required checks `build`, `integration` and `judge-verdict` pass,
and shows `app/github-actions` as the merger. The guards, decided in #355 and #366, run first: the pull request is
open with the label `repair`, its head is `repair/*` or `factory/*`, its head is the SHA the Copilot stage settled
on, and `judge-verdict` is success on that head. The job waits for the judge when its check run is not complete yet.
A verdict other than success is one comment on the issue and no merge.

The loop bound: the job merges nothing on a pull request whose head branch is the branch of the red run that
started the repair. The run is the `Run:` line under `## Run` of the issue, which read.py wrote.

A merge by GITHUB_TOKEN closes no issue named by `Closes #n`, and `--delete-branch` deletes nothing on a deferred
merge, as #361 found. So the job waits for the merge, closes the issue with one comment that names the merge
commit, and deletes the head branch when `delete_branch_on_merge` left it. The job reads check runs with no
`checks` permission: the repo is public, and #361 bars `checks: write` on this job.

The kill switch REPAIR_ENABLED is read first, on every event. Any value other than `true` ends the step with the
exit code 0 and one line.

Environment: GH_TOKEN, GITHUB_REPOSITORY, REPAIR_ENABLED, REPAIR_MERGE_TIMEOUT_SECONDS (default 7200),
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
FIELDS = "number,state,headRefName,headRefOid,body,labels,url,mergeCommit,mergedBy"


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


def wait_for_merge(repo, number, deadline, wait):
    while True:
        pr = read_pull_request(repo, number)
        if pr["state"] == "MERGED":
            return pr
        if pr["state"] != "OPEN":
            sys.exit(f"repair: pull request #{number} is {pr['state'].lower()} before the merge")
        if time.time() >= deadline:
            sys.exit(f"repair: pull request #{number} is not merged after the timeout; auto-merge stays armed, "
                     f"and the owner closes its issue after the merge")
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


def delete_branch(repo, branch):
    """The fallback for a repo with delete_branch_on_merge off: the head branch goes when it still exists."""
    refs = json.loads(gh("api", f"repos/{repo}/git/matching-refs/heads/{branch}"))
    if f"refs/heads/{branch}" not in [ref["ref"] for ref in refs]:
        return
    gh("api", "--method", "DELETE", f"repos/{repo}/git/refs/heads/{branch}")
    print(f"repair: deleted the branch {branch}")


def main():
    repo = os.environ.get("GITHUB_REPOSITORY") or sys.exit("repair: GITHUB_REPOSITORY is not set")
    if os.environ.get("REPAIR_ENABLED") != "true":
        print("repair: REPAIR_ENABLED is not true; no merge")
        return
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
    deadline = time.time() + int(os.environ.get("REPAIR_MERGE_TIMEOUT_SECONDS", "7200"))
    conclusion = wait_for_judge(repo, sha, deadline, wait)
    if conclusion != "success":
        stop(repo, number, issue, sha,
             f"The check `{CHECK}` on `{sha}` of pull request #{number} is `{conclusion}`. No merge. The owner reads the judge's review.")
        return
    if read_pull_request(repo, number)["headRefOid"] != sha:
        print(f"repair: the head of pull request #{number} moved off {sha[:7]} while the judge ran; nothing to merge")
        return

    gh("pr", "merge", str(number), "-R", repo, "--auto", "--squash", "--delete-branch")
    print(f"repair: armed the auto-merge of pull request #{number} on {sha[:7]}")
    merged = wait_for_merge(repo, number, deadline, wait)
    close_issue(repo, issue, merged)
    delete_branch(repo, pr["headRefName"])


if __name__ == "__main__":
    main()
