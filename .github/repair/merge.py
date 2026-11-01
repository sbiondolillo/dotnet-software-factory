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

The path rule, decided in #406 and in its addendum of 2026-11-01: the job merges no repair pull request whose diff
touches a path of GUARDED. GUARDED holds every path under .github/workflows/ except factory.yml, and .github/judge/,
.github/repair/ and .claude/skills/change/JUDGES.md. The refusal is one comment on the issue that names each such
path. A workflow on pull_request runs from the pull request's own copy. A workflow with `checks: write` can post a
green judge-verdict, and verdict() reads that check by its name alone. judge.py reads .github/judge/ and JUDGES.md as
the rules of the judge. So a repair that adds or edits a workflow, or edits those files, judges itself. repair.yml
and .github/repair/ run from main, so an edit there acts only after a merge, and this rule is in .github/repair/. So
a repair that edits the rule is refused by the rule. A change to a path of GUARDED stays the owner's direct change.
.github/agents/, src/, sandbox/ and the other paths are open to a repair. The rule runs before the wait for the
judge, so a refused pull request spends no wait and starts no gate run.

factory.yml is open, so a fault in it stays repairable, except for its `on:` key and its `permissions:` keys. The
job reads factory.yml at the base SHA and at the head SHA from the contents API. It uses no YAML library, since the
job installs nothing. So the check reads text, and it fails closed: a form that it cannot read is a refusal. It
refuses a diff of factory.yml for each of three causes, and the comment names each cause.

A text check sees no change in a block whose lines did not change. A new line that opens a quote above the top-level
`permissions:` block, closed after it, swallows the block into a scalar, and the file then parses with no top-level
permissions. A removed block gives the token the repo's default. So the job reads `actions/permissions/workflow` when
the diff touches factory.yml, and exits when `default_workflow_permissions` is not `read`. The default is a repo
setting of the owner, so the exit is one line and no comment.

1. A changed line, which is a line that the diff of the two texts adds or removes, that holds a key form. Before the
   match, the job strips a comment from the line when the line holds no quote character. The forms are: the token
   `permissions` anywhere, in any case and in any quoting; `on` as a key at column 0, in quotes, after a tag or an
   anchor, and with spaces or tabs before its colon; a flow collection, `{` or `[`, where a node starts; an anchor
   `&`, an alias `*` or a tag `!` where a node starts; an explicit key `? `; and a backslash after a double quote. A
   node starts at the start of a line after its indent, after `- `, `? ` or `, `, and after a `:` and its spaces.
   A node inside a flow collection needs no match, since the start of that collection is a refusal. `${{ }}` and
   `.resources["x"]` start no node, so a step edit with an expression passes.
2. The block of `on:` at column 0 differs. The block of a key is its line and each later line indented deeper than
   the key. A line at the key's own indent that starts with `- ` stays in the block, for a sequence at that indent.
   Blank lines and lines that hold only a comment are left out. So an added trigger under `on:` is a refusal, though
   its line holds no key form.
3. The block of a `permissions:` key at any depth differs, by the same rule. So `checks: read` changed to
   `checks: write` under the top-level key is a refusal.

The check refuses more than the two keys. A step edit that holds `permissions` in a scalar, such as
`- run: 'echo permissions: read'`, is a refusal, and so is a step edit with a flow collection, an anchor or a tag.
Such a refusal is false, and it is accepted: that edit becomes the owner's direct change. A comment-only edit and a
plain step edit pass, so the planted fault of #407 stays repairable. factory.yml on main holds no anchor, alias, tag,
explicit key or escape, and each flow collection on main closes on its own line, so no line of a flow on main
changes without its `{` or `[`. The base SHA is the tip of the base branch and not the merge base, so a change
on main after the branch point is a changed line too. A file that is absent at a SHA has no lines, so a diff that adds
or deletes factory.yml is a refusal.

The base rule: the job reads the diff against the base of the pull request, and a retarget changes that diff with no
new head. So the job requires the base to be the default branch of the repo, and refuses any other base with one
comment. It reads the pull request after the list of files and again directly before it arms the merge, and it exits
with no comment when the base or the head moved since the check. While the merge is armed, a move of the base disarms
it, as a move of the head does.

The job reads the paths from `GET /repos/<repo>/pulls/<n>/files`, with `--paginate`, and not from
`gh pr diff --name-only`. The API names both paths of a rename, `filename` and `previous_filename`, so a move of a
guarded file is seen. `gh pr diff` reads the diff media type, which GitHub refuses for a large diff. The API lists at
most 3000 files, so a list shorter than `changedFiles` of the pull request is a refusal too. The job reads the head
again after the list, so the list belongs to the settled SHA.

The loop bound: the job merges nothing on a pull request whose head branch is the branch of the red run that
started the repair. The run is the `Run:` line under `## Run` of the issue, which read.py wrote.

A merge by GITHUB_TOKEN closes no issue named by `Closes #n`, and `--delete-branch` deletes nothing on a deferred
merge, as #361 found. So the job waits for the merge, closes the issue with one comment that names the merge commit,
and deletes the head branch when `delete_branch_on_merge` left it. A push by GITHUB_TOKEN starts no workflow run, so
the job then starts `ci` on the base branch with workflow_dispatch, and the merge commit gets its run on `main`. A
wait that reaches the timeout disarms the auto-merge, so no later head merges unsettled. The job reads check runs with
no `checks` permission: the repo is public, and #361 bars `checks: write` on this job.

The gate, decided in #399 Q5 and built in #404: a repair of the factory itself merges only after its story runs green
through the repaired factory. A pull request is a factory repair when it or its issue has the label `factory`. The
issue has it from read.py, and the pull request has only `repair` from build.py, so the job reads both. After the
judge approved, the job reads the story from the line `Story: #<n>` of the issue body, which read.py wrote. It
dispatches factory.yml at the head branch with `issue=<story>` and `base=main`, so the factory of the repair branch
builds the story on the head of main, with the sandbox image from `sandbox/` of that branch. The run's name is
`#<story>`. The job finds the run as build.py does: a new run of the branch with that name, and it must be on the
settled SHA. The gate is a run and not a check run, since #361 bars `checks: write` on this job. On `success` the
merge arms. Any other conclusion is one comment on the issue that names the run, and no merge. A factory repair with
no issue or no story line gets one comment and no merge. A pull request with no `factory` label, a ci repair, gets
no gate run. The story's own pull request opens as a side effect of a green gate run.

The kill switch REPAIR_ENABLED is read first, on every event. Any value other than `true` ends the step with the
exit code 0 and one line.

The merge timeout covers the wait for the judge and the wait for the merge. The gate timeout covers the wait for the
gate run, and that wait does not count against the merge timeout. The job has six waits. Their defaults are the
rerun 3600 s, the reader 900 s, the build 3600 s, the Copilot stage 7200 s, the gate 3600 s and the merge 1800 s.
They add up to 20700 s. That is 900 s under GitHub's job limit of 21600 s, for the search of at most 180 s for the
gate run and for the other steps of the job. The gate made the merge timeout 1800 s, down from 3600 s. The judge and
the required checks start on each push, so in the usual case they ran during the Copilot stage, and the merge lands
at once. A green run of factory.yml on a story took about 1350 s in October 2026.

Environment: GH_TOKEN, GITHUB_REPOSITORY, REPAIR_ENABLED, REPAIR_MERGE_TIMEOUT_SECONDS (default 1800),
REPAIR_GATE_TIMEOUT_SECONDS (default 3600), REPAIR_POLL_SECONDS (default 30).
Arguments: the number of the repair pull request, and the head SHA the Copilot stage settled on.
"""

import difflib
import json
import os
import re
import signal
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
from judge import gh  # noqa: E402

LABEL = "repair"
FACTORY_LABEL = "factory"
WORKFLOW = "factory.yml"
HEADS = ("repair/", "factory/")
CHECK = "judge-verdict"
MARKER = "<!-- repair-merge:{sha} -->"
FIELDS = "number,state,headRefName,headRefOid,baseRefName,baseRefOid,body,labels,url,mergeCommit,mergedBy,changedFiles"
# The paths a repair never changes, as #406 and its addendum decided. An entry that ends with a slash holds each path
# under it. OPEN is the exception to GUARDED. A diff of FACTORY changes no key of KEYS, which holds each key with
# whether it counts at column 0 alone.
GUARDED = (
    ".github/workflows/",
    ".github/judge/",
    ".github/repair/",
    ".claude/skills/change/JUDGES.md",
)
FACTORY = ".github/workflows/factory.yml"
OPEN = (FACTORY,)
KEYS = (("on", True), ("permissions", False))
UNREADABLE = "a line in a form the job cannot read"
# A tag or an anchor before a key, such as `!!str ` or `&a `.
PREFIX = r"(?:[!&][^ \t]*[ \t]+)*"
# A comment: a `#` at the start of a line or after a space or a tab, to the end of the line.
COMMENT = re.compile(r"(?:^|[ \t])#.*$")
# A key form of the module docstring, in a changed line of factory.yml.
PERMISSIONS = re.compile(r"permissions", re.I)
ON = re.compile(rf"""^{PREFIX}(?P<quote>["']?)on(?P=quote)[ \t]*:""", re.I)
# Where a node starts, then a flow collection, a tag, an anchor, an alias or an explicit key; or a backslash after a
# double quote.
NODE = r"(?:^[ \t]*|[-?,][ \t]+|:[ \t]*)"
FORM = re.compile(rf"""{NODE}(?:[\[{{!&*]|\?(?:[ \t]|$))|"[^"]*\\""")


def read_pull_request(repo, number):
    return json.loads(gh("pr", "view", str(number), "-R", repo, "--json", FIELDS))


def default_branch(repo):
    return gh("api", f"repos/{repo}", "--jq", ".default_branch").strip()


def default_permissions(repo):
    """The repo's default_workflow_permissions, `read` or `write`."""
    return gh("api", f"repos/{repo}/actions/permissions/workflow", "--jq", ".default_workflow_permissions").strip()


def closing_issue(body):
    match = re.search(r"^Closes #(\d+)\s*$", body or "", re.M)
    return int(match.group(1)) if match else None


def read_issue(repo, issue):
    return json.loads(gh("issue", "view", str(issue), "-R", repo, "--json", "body,labels"))


def guarded(paths):
    """Each path of the list that GUARDED holds and OPEN does not, sorted, and each one once."""
    return sorted(
        {
            path
            for path in paths
            if path not in OPEN
            and any(path == entry or (entry.endswith("/") and path.startswith(entry)) for entry in GUARDED)
        }
    )


def key_blocks(text, key, top_level):
    """The blocks of the key in the YAML text, each as a list of lines, in the order of the file. The module
    docstring states what a block is."""
    start = re.compile(rf"""^(?P<indent> *(?:-[ \t]+)*){PREFIX}(?P<quote>["']?){key}(?P=quote)[ \t]*:""", re.I)
    lines = [line.rstrip() for line in (text or "").splitlines()]
    blocks = []
    for index, line in enumerate(lines):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        match = start.match(line)
        if match and not (top_level and match["indent"]):
            depth = len(match["indent"])
            block = [line]
            for later in lines[index + 1 :]:
                stripped = later.lstrip()
                if not stripped or stripped.startswith("#"):
                    continue
                indent = len(later) - len(stripped)
                if indent < depth or (indent == depth and not (stripped.startswith("- ") or stripped == "-")):
                    break
                block.append(later)
            blocks.append(block)
    return blocks


def changed_lines(base, head):
    """Each line that the diff of the two texts adds or removes, in the order of the diff. A text of None has no
    lines."""
    old = (base or "").splitlines()
    new = (head or "").splitlines()
    matcher = difflib.SequenceMatcher(None, old, new, autojunk=False)
    return [line for tag, i1, i2, j1, j2 in matcher.get_opcodes() if tag != "equal" for line in old[i1:i2] + new[j1:j2]]


def line_causes(line):
    """The causes of the module docstring that the changed line holds, as a set of `on:`, `permissions:` and
    UNREADABLE. The comment goes first when the line holds no quote character."""
    text = line if "'" in line or '"' in line else COMMENT.sub("", line)
    causes = set()
    if PERMISSIONS.search(text):
        causes.add("permissions:")
    if ON.match(text):
        causes.add("on:")
    if FORM.search(text):
        causes.add(UNREADABLE)
    return causes


def changed_keys(base, head):
    """The causes of a refusal of the two texts of factory.yml, in the order `on:`, `permissions:`, UNREADABLE. A
    text of None is a file that is absent at that SHA."""
    causes = set().union(*(line_causes(line) for line in changed_lines(base, head)))
    causes.update(f"{key}:" for key, top in KEYS if key_blocks(base, key, top) != key_blocks(head, key, top))
    return [cause for cause in ("on:", "permissions:", UNREADABLE) if cause in causes]


def file_at(repo, path, sha):
    """The text of the file at the commit, or None when the commit holds no such file. Any other failure ends the job,
    so a failed read merges nothing."""
    try:
        return gh("api", "-H", "Accept: application/vnd.github.raw+json", f"repos/{repo}/contents/{path}?ref={sha}")
    except SystemExit as error:
        if "HTTP 404" in str(error):
            return None
        raise


def refused(paths, keys):
    """The items of the refusal comment: each guarded path, then FACTORY with each changed key."""
    items = [f"`{path}`" for path in paths]
    named = [key for key in keys if key != UNREADABLE]
    if named:
        names = " and ".join(f"`{key}`" for key in named)
        items.append(f"the {names} {'key' if len(named) == 1 else 'keys'} of `{FACTORY}`")
    if UNREADABLE in keys:
        items.append(f"{UNREADABLE} in `{FACTORY}`")
    return items


def changed_files(repo, number):
    """The files of the diff of the pull request, as (the count of entries, every path). A rename gives both paths.
    Each line of the output is one entry as a JSON array, so a path that holds a line feed stays one path."""
    lines = gh(
        "api",
        f"repos/{repo}/pulls/{number}/files?per_page=100",
        "--paginate",
        "--jq",
        ".[] | [.filename, .previous_filename] | @json",
    ).splitlines()
    entries = [json.loads(line) for line in lines if line]
    return len(entries), [path for entry in entries for path in entry if path]


def red_run_branch(repo, body):
    """The head branch of the red run named under ## Run of the issue body, or None when the body names no run."""
    match = re.search(rf"^Run: https://github\.com/{re.escape(repo)}/actions/runs/(\d+)\s*$", body or "", re.M)
    if not match:
        return None
    return json.loads(gh("api", f"repos/{repo}/actions/runs/{match.group(1)}"))["head_branch"]


def verdict(repo, sha):
    """The latest check run named judge-verdict on the commit: (status, conclusion), or None when the judge
    posted none."""
    answer = json.loads(gh("api", f"repos/{repo}/commits/{sha}/check-runs?check_name={CHECK}&filter=latest"))
    runs = answer.get("check_runs") or []
    return (runs[0]["status"], runs[0].get("conclusion")) if runs else None


def story_of(body):
    """The number of the story under the line `Story: #<n>` of the issue body, or None."""
    match = re.search(r"^Story: #(\d+)\s*$", body or "", re.M)
    return int(match.group(1)) if match else None


def gate_runs(repo, branch):
    runs = json.loads(
        gh(
            "run",
            "list",
            "-R",
            repo,
            "--workflow",
            WORKFLOW,
            "--branch",
            branch,
            "--event",
            "workflow_dispatch",
            "--limit",
            "30",
            "--json",
            "databaseId,headSha,displayTitle,url",
        )
    )
    return {run["databaseId"]: run for run in runs}


def dispatch_gate(repo, branch, number, timeout=180):
    """Starts factory.yml at the branch on the story, and gives back its run once the list holds it.

    The run is new since the dispatch, on the branch, and named `#<story>` by the run-name of factory.yml.
    """
    before = gate_runs(repo, branch)
    gh("workflow", "run", WORKFLOW, "-R", repo, "--ref", branch, "-f", f"issue={number}", "-f", "base=main")
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(10)
        new = [
            run
            for run_id, run in gate_runs(repo, branch).items()
            if run_id not in before and run["displayTitle"] == f"#{number}"
        ]
        if new:
            return new[0]
    sys.exit(f"repair: the dispatch of {WORKFLOW} at {branch} for the story #{number} shows no run after {timeout} s")


def wait_for_gate(repo, run, deadline, wait):
    """The conclusion of the gate run once it completed. The job exits when the run is not complete by the deadline."""
    while True:
        current = json.loads(gh("run", "view", str(run["databaseId"]), "-R", repo, "--json", "status,conclusion"))
        if current["status"] == "completed":
            return current["conclusion"]
        if time.time() >= deadline:
            sys.exit(
                f"repair: the gate run {run['url']} is {current['status']} after the timeout; "
                "it goes on, and no merge arms"
            )
        print(f"repair: the gate run {run['url']} is {current['status']}; waiting")
        time.sleep(wait)


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


def arm_and_wait(repo, number, sha, base, deadline, wait):
    """Arms the auto-merge and waits for the merge. Any exit before the merge, a timeout, a closed pull request, a
    failed read or a cancel of the job, disarms the auto-merge first, so no armed merge outlives the step. The arm is
    inside the try, so a cancel during the arm command disarms too. A cancel sends SIGINT, which is KeyboardInterrupt,
    then SIGTERM, which the handler turns into an exit."""
    signal.signal(signal.SIGTERM, lambda *_: sys.exit("repair: the job was cancelled"))
    try:
        gh("pr", "merge", str(number), "-R", repo, "--auto", "--squash", "--delete-branch", "--match-head-commit", sha)
        print(f"repair: armed the auto-merge of pull request #{number} on {sha[:7]}")
        return wait_for_merge(repo, number, sha, base, deadline, wait)
    except BaseException:
        disarm(repo, number)
        raise


def wait_for_merge(repo, number, sha, base, deadline, wait):
    """The merged pull request, or None once its head moved off the settled SHA or its base off the default branch,
    with the auto-merge disarmed."""
    while True:
        pr = read_pull_request(repo, number)
        if pr["state"] == "MERGED":
            return pr
        if pr["state"] != "OPEN":
            sys.exit(f"repair: pull request #{number} is {pr['state'].lower()} before the merge")
        if pr["headRefOid"] != sha or pr["baseRefName"] != base:
            disarm(repo, number)
            print(
                f"repair: the head or the base of pull request #{number} moved off {sha[:7]} on {base} while the "
                "auto-merge was armed; no merge"
            )
            return None
        if time.time() >= deadline:
            sys.exit(
                f"repair: pull request #{number} is not merged after the timeout; "
                f"`gh workflow run repair.yml -f pr={number}` arms the auto-merge again"
            )
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
    """A run of ci on the base branch, before the issue closes, so a failed close leaves no merge commit untested. A
    push by GITHUB_TOKEN starts no run, so the merge commit gets one here."""
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
    number = (
        int(sys.argv[1])
        if len(sys.argv) > 1 and sys.argv[1].isdigit()
        else sys.exit("repair: give the number of a repair pull request")
    )
    sha = (
        sys.argv[2]
        if len(sys.argv) > 2 and re.fullmatch(r"[0-9a-f]{40}", sys.argv[2])
        else sys.exit("repair: give the head SHA the Copilot stage settled on")
    )

    pr = read_pull_request(repo, number)
    labels = [item["name"] for item in pr["labels"]]
    if pr["state"] != "OPEN" or LABEL not in labels:
        print(f"repair: pull request #{number} is {pr['state'].lower()} with the labels {labels}; nothing to merge")
        return
    if not pr["headRefName"].startswith(HEADS):
        print(
            f"repair: the head {pr['headRefName']} of pull request #{number} is not repair/* or factory/*; "
            "nothing to merge"
        )
        return
    if pr["headRefOid"] != sha:
        print(
            f"repair: the head of pull request #{number} is {pr['headRefOid'][:7]}, not the settled {sha[:7]}; "
            "nothing to merge"
        )
        return
    issue = closing_issue(pr["body"])
    default = default_branch(repo)
    if pr["baseRefName"] != default:
        stop(
            repo,
            number,
            issue,
            sha,
            f"Pull request #{number} targets `{pr['baseRefName']}`, not `{default}`. The job reads the diff "
            f"against the base, so a repair targets `{default}` alone. No merge. The owner reads the pull request.",
        )
        return
    fields = read_issue(repo, issue) if issue is not None else {"body": "", "labels": []}
    gated = FACTORY_LABEL in labels or FACTORY_LABEL in [item["name"] for item in fields.get("labels") or []]
    count, paths = changed_files(repo, number)
    after = read_pull_request(repo, number)
    if after["headRefOid"] != sha or after["baseRefName"] != default:
        print(
            f"repair: the head or the base of pull request #{number} moved off {sha[:7]} on {default} while the "
            "job read its files; nothing to merge"
        )
        return
    if FACTORY in paths and (setting := default_permissions(repo)) != "read":
        sys.exit(
            f"repair: the repo's default_workflow_permissions is {setting}, not read. The key rule of {FACTORY} "
            "rests on read: a diff that removes its permissions block passes the rule. No merge."
        )
    keys = (
        changed_keys(file_at(repo, FACTORY, after["baseRefOid"]), file_at(repo, FACTORY, sha))
        if FACTORY in paths
        else []
    )
    touched = refused(guarded(paths), keys)
    if touched or count < after["changedFiles"]:
        stop(
            repo,
            number,
            issue,
            sha,
            f"Pull request #{number} changes {', '.join(touched)}. "
            f"A repair changes no workflow file other than `{FACTORY}`, no `on:` or `permissions:` key of it, "
            "no line of it in a form the job cannot read, "
            "no file of the judge and no file of the repair loop. A workflow on pull_request runs from the pull "
            "request's own copy and can post the judge's check, so such a repair judges itself, and the files of "
            "the loop hold this rule. No merge. A change to these files is the owner's direct change."
            if touched
            else f"The list of the files of pull request #{number} holds {count} of its {after['changedFiles']} "
            "files, so the job cannot read whether it changes a file that the rule of #406 guards. "
            "No merge. The owner reads the pull request.",
        )
        return
    if issue is not None and red_run_branch(repo, fields["body"]) == pr["headRefName"]:
        stop(
            repo,
            number,
            issue,
            sha,
            f"The head `{pr['headRefName']}` of pull request #{number} is the branch of the red run "
            "that started this repair. "
            "The loop merges no branch of its own red run. The owner reads the pull request.",
        )
        return
    story_number = story_of(fields["body"])
    if gated and story_number is None:
        stop(
            repo,
            number,
            issue,
            sha,
            f"Pull request #{number} is a repair of the factory, and its issue names no story in a line `Story: #<n>`. "
            "No gate run, and no merge. The owner reads the issue."
            if issue is not None
            else f"Pull request #{number} is a repair of the factory, and it closes no issue, so it has no story. "
            "No gate run, and no merge. The owner reads the pull request.",
        )
        return

    wait = int(os.environ.get("REPAIR_POLL_SECONDS", "30"))
    deadline = time.time() + int(os.environ.get("REPAIR_MERGE_TIMEOUT_SECONDS", "1800"))
    conclusion = wait_for_judge(repo, sha, deadline, wait)
    if conclusion != "success":
        stop(
            repo,
            number,
            issue,
            sha,
            f"The check `{CHECK}` on `{sha}` of pull request #{number} is `{conclusion}`. No merge. "
            "The owner reads the judge's review.",
        )
        return
    if gated:
        left = deadline - time.time()
        run = dispatch_gate(repo, pr["headRefName"], story_number)
        print(f"repair: started the gate run {run['url']} of the story #{story_number}")
        if run["headSha"] != sha:
            print(f"repair: the gate run {run['url']} is on {run['headSha'][:7]}, not the settled {sha[:7]}; no merge")
            return
        gate_deadline = time.time() + int(os.environ.get("REPAIR_GATE_TIMEOUT_SECONDS", "3600"))
        conclusion = wait_for_gate(repo, run, gate_deadline, wait)
        if conclusion != "success":
            stop(
                repo,
                number,
                issue,
                sha,
                f"The gate run {run['url']} of the story #{story_number} through the factory of `{sha}` "
                f"is `{conclusion}`. No merge of pull request #{number}. The owner reads the run.",
            )
            return
        print(f"repair: the gate run {run['url']} is success")
        deadline = time.time() + left
    current = read_pull_request(repo, number)
    if current["headRefOid"] != sha or current["baseRefName"] != default:
        print(
            f"repair: the head or the base of pull request #{number} moved off {sha[:7]} on {default} while the judge"
            f"{' and the gate' if gated else ''} ran; nothing to merge"
        )
        return

    merged = arm_and_wait(repo, number, sha, default, deadline, wait)
    if merged is None:
        return
    if merged["headRefOid"] != sha:
        stop(
            repo,
            number,
            issue,
            sha,
            f"Pull request #{number} merged at `{merged['headRefOid']}`, not at the settled `{sha}`. "
            "The job closes no issue for it. The owner reads the pull request.",
        )
        return
    start_ci(repo, merged["baseRefName"])
    close_issue(repo, issue, merged)
    delete_branch(repo, pr["headRefName"])


if __name__ == "__main__":
    main()
