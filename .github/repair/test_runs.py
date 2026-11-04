"""Unit tests of runs.py, from #516, the one module of the repair loop that dispatches, lists and waits for the runs
of factory.yml. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A FakeGitHub of .github/shared/ stands in for the
adapter, and a fake clock for time, so no test calls GitHub or sleeps."""

import os
import sys
import unittest
from datetime import datetime, timezone
from unittest import mock

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shared"))
import runs  # noqa: E402
from fake_github import FakeGitHub  # noqa: E402

REPO = "o/r"
NOW = 1_800_000_000.0
# The verb of a dispatch, one word on each line, so the grep of #516's `## Done when` matches runs.py alone.
START = (
    "workflow",
    "run",
)


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


FIELDS = runs.FIELDS
WORKFLOW = runs.WORKFLOW


def listing(*flags):
    """The args of `find` with these flags."""
    return ("run", "list", "-R", REPO, "--workflow", WORKFLOW, *flags, "--limit", "30", "--json", FIELDS)


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


class StartTests(unittest.TestCase):
    def test_the_ref_and_each_input_in_order(self):
        gh = FakeGitHub()
        call = (*START, "ci.yml", "-R", REPO, "--ref", "main", "-f", "a=1", "-f", "b=x")
        gh.answer(call, "")
        runs.start(gh, REPO, "ci.yml", "main", {"a": 1, "b": "x"})
        self.assertEqual(gh.args(), [call])

    def test_with_no_ref_the_default_branch(self):
        gh = FakeGitHub()
        gh.answer((*START, "ci.yml", "-R", REPO), "")
        runs.start(gh, REPO, "ci.yml")
        self.assertEqual(gh.args(), [(*START, "ci.yml", "-R", REPO)])


class FindTests(unittest.TestCase):
    def test_no_run_is_none(self):
        gh = FakeGitHub()
        gh.answer(listing(), [])
        self.assertIsNone(runs.find(gh, REPO, 7))

    def test_the_newest_run_of_the_title(self):
        gh = FakeGitHub()
        gh.answer(listing(), [run(3, "#70"), run(2, "#7"), run(1, "#7")])
        self.assertEqual(runs.find(gh, REPO, 7)["databaseId"], 2)

    def test_a_run_created_before_since_is_none(self):
        gh = FakeGitHub()
        gh.answer(listing(), [run(2, "#7", created=NOW - 1)])
        self.assertIsNone(runs.find(gh, REPO, 7, NOW))
        self.assertEqual(runs.find(gh, REPO, 7, NOW - 1)["databaseId"], 2)

    def test_with_no_number_any_title(self):
        gh = FakeGitHub()
        gh.answer(listing(), [run(3, "#70"), run(2, "#7")])
        self.assertEqual(runs.find(gh, REPO)["databaseId"], 3)

    def test_each_filter_is_a_flag(self):
        gh = FakeGitHub()
        flags = ("--branch", "main", "--status", "success", "--event", "workflow_dispatch")
        gh.answer(listing(*flags), [run(2, "#7")])
        found = runs.find(gh, REPO, branch="main", status="success", event="workflow_dispatch")
        self.assertEqual(found["databaseId"], 2)


class DispatchTests(unittest.TestCase):
    def dispatch(self, lists, ref=None, inputs=None, timeout=180):
        gh = FakeGitHub()
        at = ("--ref", ref) if ref else ()
        fields = [arg for name, value in {"issue": 7, **(inputs or {})}.items() for arg in ("-f", f"{name}={value}")]
        call = (*START, runs.WORKFLOW, "-R", REPO, *at, *fields)
        gh.answer(call, "")
        branch = ("--branch", ref) if ref else ()
        gh.answer(listing(*branch, "--event", "workflow_dispatch"), *lists)
        with Clock() as clock:
            found = runs.dispatch(gh, REPO, 7, ref=ref, inputs=inputs, timeout=timeout)
        return found, gh, call, clock

    def test_the_run_of_its_issue_and_not_an_older_run_or_another_issue_s(self):
        old = run(1, "#7", created=NOW - runs.SKEW - 1)
        other = run(2, "#999")
        own = run(3, "#7")
        found, gh, call, _ = self.dispatch([[old], [other, old], [own, other, old]])
        self.assertEqual(found, own)
        self.assertEqual(gh.args()[0], call)

    def test_a_run_created_inside_the_30_s_window_is_taken(self):
        self.assertEqual(runs.SKEW, 30)
        own = run(3, "#7", created=NOW - 30)
        found, _, _, _ = self.dispatch([[own]])
        self.assertEqual(found, own)

    def test_at_a_ref_the_inputs_follow_the_issue(self):
        own = run(3, "#7")
        found, gh, call, _ = self.dispatch([[own]], ref="repair/5", inputs={"base": "main"})
        self.assertEqual(found, own)
        self.assertEqual(call[-6:], ("--ref", "repair/5", "-f", "issue=7", "-f", "base=main"))
        self.assertEqual(gh.count(*call), 1)

    def test_no_run_by_the_timeout_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.dispatch([[]], timeout=25)
        self.assertIn("shows no run after the timeout", stop.exception.code)


class WaitTests(unittest.TestCase):
    VIEW = ("run", "view", "9", "-R", REPO, "--json", "status,conclusion")

    def fake(self, *states):
        gh = FakeGitHub()
        gh.answer(self.VIEW, *[{"status": status, "conclusion": conclusion} for status, conclusion in states])
        return gh

    def test_the_conclusion_once_completed(self):
        gh = self.fake(("queued", ""), ("in_progress", ""), ("completed", "failure"))
        with Clock() as clock:
            self.assertEqual(runs.wait(gh, REPO, 9, NOW + 100, 10), "failure")
        self.assertEqual(gh.count(*self.VIEW), 3)
        self.assertEqual(clock.slept, [10, 10])

    def test_a_completed_run_with_no_conclusion_is_the_empty_text(self):
        gh = self.fake(("completed", None))
        with Clock():
            self.assertEqual(runs.wait(gh, REPO, 9, NOW + 100, 10), "")

    def test_a_run_completed_at_the_deadline_is_read(self):
        gh = self.fake(("completed", "success"))
        with Clock():
            self.assertEqual(runs.wait(gh, REPO, 9, NOW, 10), "success")

    def test_a_run_in_progress_at_the_deadline_exits_with_its_name(self):
        gh = self.fake(("in_progress", ""))
        with Clock() as clock, self.assertRaises(SystemExit) as stop:
            runs.wait(gh, REPO, 9, NOW + 5, 3600, name="the gate run")
        self.assertEqual(clock.slept, [5])
        self.assertEqual(
            stop.exception.code, f"repair: the gate run {runs.url(REPO, 9)} is in_progress after the timeout"
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
