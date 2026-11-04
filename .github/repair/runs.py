"""The runs of the repair loop: the one module that starts a workflow, lists the runs of factory.yml and waits for a
run, over the GitHub adapter of .github/shared/github.py.

- `start` dispatches a workflow at a ref with its inputs, and gives back nothing. merge.py starts `ci` with it, and
  read.py starts the story once more on a flake.
- `dispatch` starts factory.yml on an issue and gives back the run it started: the newest run titled `#<n>` by the
  run-name of factory.yml, created since the dispatch, on the ref of the dispatch. The title check is #469's, so a
  run of another issue dispatched in the same window is not taken.
- `find` gives the newest run of factory.yml that matches its filters, or None. Each filter left out matches any run.
- `wait` gives the conclusion of a run once its status is `completed`.
- `poll` is the one deadline loop that the waits of the repair loop share.

`since` is a time from time.time(). `dispatch` takes it SKEW seconds before the dispatch, since `createdAt` is
GitHub's clock and second-accurate, and the runner's clock can run ahead of it. An older run titled `#<n>` inside that
window would be taken, and none exists: the concurrency group of repair.yml holds one job for each issue, and each
job takes minutes.

`poll` exits the job with `repair: <state> after the timeout` when the deadline passes first, so each wait ends the
step with one line, as the hand-written loops it replaces did.
"""

import json
import sys
import time
from datetime import datetime

WORKFLOW = "factory.yml"
FIELDS = "databaseId,displayTitle,headSha,status,conclusion,url,createdAt"
LIMIT = "30"
SKEW = 30


def url(repo, run_id):
    return f"https://github.com/{repo}/actions/runs/{run_id}"


def start(gh, repo, workflow, ref=None, inputs=None):
    """Dispatches the workflow at the ref, or at the default branch with none, with each input as `-f name=value`."""
    at = ["--ref", ref] if ref else []
    fields = [arg for name, value in (inputs or {}).items() for arg in ("-f", f"{name}={value}")]
    gh.run("workflow", "run", workflow, "-R", repo, *at, *fields)


def find(gh, repo, number=None, since=None, *, branch=None, status=None, event=None):
    """The newest run of factory.yml titled `#<number>`, created at `since` or later, on the branch, with the status
    and of the event, or None. Each filter left out matches any run."""
    flags = [
        arg
        for flag, value in (("--branch", branch), ("--status", status), ("--event", event))
        if value
        for arg in (flag, value)
    ]
    runs = json.loads(
        gh.run("run", "list", "-R", repo, "--workflow", WORKFLOW, *flags, "--limit", LIMIT, "--json", FIELDS)
    )
    for run in runs:
        if number is not None and run["displayTitle"] != f"#{number}":
            continue
        if since is not None and created(run) < since:
            continue
        return run
    return None


def created(run):
    """The time of `createdAt`, in the seconds of time.time()."""
    return datetime.fromisoformat(run["createdAt"].replace("Z", "+00:00")).timestamp()


def dispatch(gh, repo, number, *, ref=None, inputs=None, timeout=180, every=10):
    """Starts factory.yml on issue <number> at the ref, with `issue=<number>` and then each input, and gives back the
    run it started once the list holds it. The job exits when no such run shows within the timeout."""
    since = time.time() - SKEW
    start(gh, repo, WORKFLOW, ref, {"issue": number, **(inputs or {})})
    where = f" at {ref}" if ref else ""
    return poll(
        lambda: (
            find(gh, repo, number, since, branch=ref, event="workflow_dispatch"),
            f"the dispatch of {WORKFLOW}{where} for #{number} shows no run",
        ),
        time.time() + timeout,
        every,
    )


def read(gh, repo, run_id):
    """The status and the conclusion of the run."""
    return json.loads(gh.run("run", "view", str(run_id), "-R", repo, "--json", "status,conclusion"))


def wait(gh, repo, run_id, deadline, every, name="the run"):
    """The conclusion of the run once its status is `completed`. The job exits when the deadline passes first."""

    def state():
        run = read(gh, repo, run_id)
        done = run["status"] == "completed"
        return (run["conclusion"] or "") if done else None, f"{name} {url(repo, run_id)} is {run['status']}"

    return poll(state, deadline, every)


def poll(read, deadline, every):
    """The first value that `read()` gives other than None. `read` gives (value, state), and `state` names what the
    poll waits on, such as `the run <url> is queued`. Each read before the deadline that gives None prints the state
    and sleeps `every` seconds, cut to the deadline. The read at or past the deadline that gives None exits the job
    with the state."""
    while True:
        value, state = read()
        if value is not None:
            return value
        if time.time() >= deadline:
            sys.exit(f"repair: {state} after the timeout")
        print(f"repair: {state}; waiting")
        time.sleep(max(0, min(every, deadline - time.time())))
