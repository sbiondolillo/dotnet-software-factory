"""Unit tests of runs.py, from #516, the one module of the repair loop that dispatches, lists and waits for the runs
of factory.yml. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A FakeRepo of .github/shared/ stands in for the
repo, and a fake clock for time, so no test calls GitHub or sleeps. The argument lists of a dispatch, a list and a read
of a run are facts of repo.py, and test_repo.py tests them."""

import os
import sys
import unittest
from datetime import datetime, timezone
from unittest import mock

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shared"))
import runs  # noqa: E402
from fake_repo import FakeRepo  # noqa: E402

NOW = 1_800_000_000.0


def iso(seconds):
    """A time as GitHub's `createdAt` prints it."""
    return datetime.fromtimestamp(seconds, timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def run(run_id, title, created=NOW, **fields):
    return {
        "databaseId": run_id,
        "displayTitle": title,
        "createdAt": iso(created),
        "url": f"https://example/{run_id}",
        "headSha": "c" * 40,
        **fields,
    }


WORKFLOW = runs.WORKFLOW


def listing(branch=None, status=None, event=None):
    """The arguments of the list of `find` with these filters."""
    return (WORKFLOW, branch, status, event)


class Clock:
    """time.time and time.sleep of runs.py: each sleep moves the clock, and is recorded."""

    def __init__(self, start=NOW):
        self.now = start
        self.slept = []

    def sleep(self, seconds):
        self.slept.append(seconds)
        self.now += seconds

    def __enter__(self):
        self.patches = (
            mock.patch.object(runs.time, "time", side_effect=lambda: self.now),
            mock.patch.object(runs.time, "sleep", side_effect=self.sleep),
        )
        for patch in self.patches:
            patch.start()
        return self

    def __exit__(self, *_):
        for patch in self.patches:
            patch.stop()


class FindTests(unittest.TestCase):
    def test_no_run_is_none(self):
        repo = FakeRepo()
        repo.answer("workflow_runs", listing(), [])
        self.assertIsNone(runs.find(repo, 7))

    def test_the_newest_run_of_the_title(self):
        repo = FakeRepo()
        repo.answer("workflow_runs", listing(), [run(3, "#70"), run(2, "#7"), run(1, "#7")])
        self.assertEqual(runs.find(repo, 7)["databaseId"], 2)

    def test_a_run_created_before_since_is_none(self):
        repo = FakeRepo()
        repo.answer("workflow_runs", listing(), [run(2, "#7", created=NOW - 1)])
        self.assertIsNone(runs.find(repo, 7, NOW))
        self.assertEqual(runs.find(repo, 7, NOW - 1)["databaseId"], 2)

    def test_with_no_number_any_title(self):
        repo = FakeRepo()
        repo.answer("workflow_runs", listing(), [run(3, "#70"), run(2, "#7")])
        self.assertEqual(runs.find(repo)["databaseId"], 3)

    def test_each_filter_goes_to_the_list(self):
        repo = FakeRepo()
        repo.answer("workflow_runs", listing("main", "success", "workflow_dispatch"), [run(2, "#7")])
        found = runs.find(repo, branch="main", status="success", event="workflow_dispatch")
        self.assertEqual(found["databaseId"], 2)


class DispatchTests(unittest.TestCase):
    def dispatch(self, lists, ref=None, inputs=None, timeout=180):
        repo = FakeRepo()
        call = {"workflow": WORKFLOW, "ref": ref, "inputs": {"issue": 7, **(inputs or {})}}
        repo.answer("dispatch_workflow", tuple(call.values()), None)
        repo.answer("workflow_runs", listing(ref, None, "workflow_dispatch"), *lists)
        with Clock() as clock:
            found = runs.dispatch(repo, 7, ref=ref, inputs=inputs, timeout=timeout)
        return found, repo, call, clock

    def test_the_run_of_its_issue_and_not_an_older_run_or_another_issue_s(self):
        old = run(1, "#7", created=NOW - runs.SKEW - 1)
        other = run(2, "#999")
        own = run(3, "#7")
        found, repo, call, _ = self.dispatch([[old], [other, old], [own, other, old]])
        self.assertEqual(found, own)
        listed = dict(zip(("workflow", "branch", "status", "event"), listing(None, None, "workflow_dispatch")))
        self.assertEqual(repo.calls[:2], [("workflow_runs", listed), ("dispatch_workflow", call)])

    def test_a_run_of_its_title_listed_before_the_dispatch_is_not_taken_inside_the_window(self):
        red = run(1, "#7", created=NOW - 5)
        own = run(3, "#7")
        found, _, _, clock = self.dispatch([[red], [red], [own, red]])
        self.assertEqual(found, own)
        self.assertEqual(clock.slept, [10])

    def test_a_run_created_inside_the_30_s_window_is_taken(self):
        self.assertEqual(runs.SKEW, 30)
        own = run(3, "#7", created=NOW - 30)
        found, _, _, _ = self.dispatch([[], [own]])
        self.assertEqual(found, own)

    def test_at_a_ref_the_inputs_follow_the_issue(self):
        own = run(3, "#7")
        found, repo, _, _ = self.dispatch([[], [own]], ref="repair/5", inputs={"base": "main"})
        self.assertEqual(found, own)
        [call] = repo.calls_of("dispatch_workflow")
        self.assertEqual(call["ref"], "repair/5")
        self.assertEqual(list(call["inputs"].items()), [("issue", 7), ("base", "main")])

    def test_no_run_by_the_timeout_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.dispatch([[]], timeout=25)
        self.assertIn("shows no run after the timeout", stop.exception.code)


class WaitTests(unittest.TestCase):
    def fake(self, *states):
        repo = FakeRepo()
        repo.answer(
            "run_status", (9,), *[{"status": status, "conclusion": conclusion} for status, conclusion in states]
        )
        return repo

    def test_the_conclusion_once_completed(self):
        repo = self.fake(("queued", ""), ("in_progress", ""), ("completed", "failure"))
        with Clock() as clock:
            self.assertEqual(runs.wait(repo, 9, NOW + 100, 10), "failure")
        self.assertEqual(repo.calls_of("run_status"), [{"run_id": 9}] * 3)
        self.assertEqual(clock.slept, [10, 10])

    def test_a_completed_run_with_no_conclusion_is_the_empty_text(self):
        repo = self.fake(("completed", None))
        with Clock():
            self.assertEqual(runs.wait(repo, 9, NOW + 100, 10), "")

    def test_a_run_completed_at_the_deadline_is_read(self):
        repo = self.fake(("completed", "success"))
        with Clock():
            self.assertEqual(runs.wait(repo, 9, NOW, 10), "success")

    def test_a_run_in_progress_at_the_deadline_exits_with_its_name(self):
        repo = self.fake(("in_progress", ""))
        with Clock() as clock, self.assertRaises(SystemExit) as stop:
            runs.wait(repo, 9, NOW + 5, 3600, name="the gate run")
        self.assertEqual(clock.slept, [5])
        self.assertEqual(
            stop.exception.code,
            "repair: the gate run https://github.com/o/r/actions/runs/9 is in_progress after the timeout",
        )


class PollTests(unittest.TestCase):
    def test_the_first_value_other_than_none(self):
        values = iter([(None, "a"), (None, "b"), (0, "c")])
        with Clock() as clock:
            self.assertEqual(runs.poll(lambda: next(values), NOW + 100, 7), 0)
        self.assertEqual(clock.slept, [7, 7])

    def test_the_sleep_is_cut_to_the_deadline(self):
        with Clock() as clock, self.assertRaises(SystemExit) as stop:
            runs.poll(lambda: (None, "x is waiting"), NOW + 10, 4)
        self.assertEqual(clock.slept, [4, 4, 2])
        self.assertEqual(stop.exception.code, "repair: x is waiting after the timeout")


if __name__ == "__main__":
    unittest.main()
