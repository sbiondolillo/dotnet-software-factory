"""The runs of factory.yml for the repair loop: the one module that finds the run a dispatch started and waits for a
run. `repo` is a Repo of .github/shared/repo.py, which starts, lists and reads the runs.

- `dispatch` starts factory.yml on an issue and gives back the run it started: the newest run titled `#<n>` by the
  run-name of factory.yml, created since the dispatch, on the ref of the dispatch, and absent from the list read
  before the dispatch. The title check is #469's, so a run of another issue dispatched in the same window is not
  taken.
- `find` gives the newest run of factory.yml that matches its filters, or None. Each filter left out matches any run.
- `wait` gives the conclusion of a run once its status is `completed`.
- `poll` is the one deadline loop that the waits of the repair loop share.

`since` is a time from time.time(). `dispatch` takes it SKEW seconds before the dispatch, since `createdAt` is
GitHub's clock and second-accurate, and the runner's clock can run ahead of it. An older run titled `#<n>` inside that
window, such as the red run that started the repair, is in the list read before the dispatch, so `exclude` drops it.
A run that GitHub lists only after that read and created before the dispatch is the one gap, and it needs two
dispatches of one title inside one window of SKEW seconds.

`poll` exits the job with `repair: <state> after the timeout` when the deadline passes first, so each wait ends the
step with one line, as the hand-written loops it replaces did.
"""

import sys
import time
from datetime import datetime

WORKFLOW = "factory.yml"
SKEW = 30


def find(repo, number=None, since=None, *, branch=None, status=None, event=None, exclude=()):
    """The newest run of factory.yml titled `#<number>`, created at `since` or later, on the branch, with the status
    and of the event, with an id outside `exclude`, or None. Each filter left out matches any run."""
    for run in repo.workflow_runs(WORKFLOW, branch, status, event):
        if number is not None and run["displayTitle"] != f"#{number}":
            continue
        if since is not None and created(run) < since:
            continue
        if exclude and run["databaseId"] in exclude:
            continue
        return run
    return None


def created(run):
    """The time of `createdAt`, in the seconds of time.time()."""
    return datetime.fromisoformat(run["createdAt"].replace("Z", "+00:00")).timestamp()


def dispatch(repo, number, *, ref=None, inputs=None, timeout=180, every=10):
    """Starts factory.yml on issue <number> at the ref, with `issue=<number>` and then each input, and gives back the
    run it started once the list holds it. The job exits when no such run shows within the timeout."""
    since = time.time() - SKEW
    before = {run["databaseId"] for run in repo.workflow_runs(WORKFLOW, ref, None, "workflow_dispatch")}
    repo.dispatch_workflow(WORKFLOW, ref, {"issue": number, **(inputs or {})})
    where = f" at {ref}" if ref else ""
    return poll(
        lambda: (
            find(repo, number, since, branch=ref, event="workflow_dispatch", exclude=before),
            f"the dispatch of {WORKFLOW}{where} for #{number} shows no run",
        ),
        time.time() + timeout,
        every,
    )


def wait(repo, run_id, deadline, every, name="the run"):
    """The conclusion of the run once its status is `completed`. The job exits when the deadline passes first."""

    def state():
        run = repo.run_status(run_id)
        done = run["status"] == "completed"
        return (run["conclusion"] or "") if done else None, f"{name} {repo.run_url(run_id)} is {run['status']}"

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
