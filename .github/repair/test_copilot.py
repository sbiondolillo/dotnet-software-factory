"""Unit tests of copilot.py, the Copilot stage of the repair loop, decided in #509. The build job of ci.yml runs them
with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A fake gh stands in for judge.gh, so no test
calls GitHub."""

import json
import os
import tempfile
import unittest
from unittest import mock

import copilot

SHA = "a" * 40
LATER = "c" * 40
RUN = "123"


class FakeGitHub:
    """Answers each gh call copilot.py makes: the pull request, the run's status in order, the comments of a target,
    and a new comment, which it stores."""

    def __init__(self, runs, state="OPEN", labels=("repair",), body="Closes #5", heads=(SHA,)):
        self.runs = list(runs)
        self.heads = list(heads)
        self.pr = {"state": state, "labels": [{"name": n} for n in labels], "body": body}
        self.comments = []
        self.calls = []

    def __call__(self, *args, input_text=None):
        self.calls.append(args)
        if args[:2] == ("pr", "view"):
            head = self.heads.pop(0) if len(self.heads) > 1 else self.heads[0]
            return json.dumps({**self.pr, "headRefOid": head})
        if args[:2] == ("run", "view"):
            if sum(call[:2] == ("run", "view") for call in self.calls) > 10:
                raise AssertionError("the step reads the run past its timeout")
            status, conclusion = self.runs.pop(0) if len(self.runs) > 1 else self.runs[0]
            return json.dumps({"status": status, "conclusion": conclusion})
        if args[0] == "api" and args[1].endswith("/comments"):
            return "\n".join(self.comments)
        if args[1] == "comment":
            self.comments.append(input_text)
            return ""
        raise AssertionError(f"unexpected gh call {args}")


class MainTests(unittest.TestCase):
    def run_main(self, fake, env=None):
        with tempfile.NamedTemporaryFile("r", suffix=".out") as out:
            values = {
                "GITHUB_REPOSITORY": "o/r",
                "FACTORY_RUN": RUN,
                "GITHUB_OUTPUT": out.name,
                "REPAIR_POLL_SECONDS": "0",
                **(env or {}),
            }
            with (
                mock.patch.dict(os.environ, values),
                mock.patch.object(copilot.sys, "argv", ["copilot.py", "7"]),
                mock.patch.object(copilot, "gh", side_effect=fake),
            ):
                copilot.main()
            return out.read()

    def test_a_run_that_passes_gives_settled_and_the_head(self):
        fake = FakeGitHub([("in_progress", ""), ("completed", "success")])
        self.assertEqual(self.run_main(fake), f"copilot=settled\nsha={SHA}\n")
        self.assertEqual(fake.comments, [])

    def test_the_head_is_read_after_the_run_ends(self):
        fake = FakeGitHub([("in_progress", ""), ("completed", "success")], heads=(SHA, LATER))
        self.assertEqual(self.run_main(fake), f"copilot=settled\nsha={LATER}\n")

    def test_a_run_that_completes_as_the_timeout_passes_is_read(self):
        fake = FakeGitHub([("completed", "success")])
        self.assertEqual(self.run_main(fake, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "0"}), f"copilot=settled\nsha={SHA}\n")

    def test_a_run_that_fails_posts_one_comment_and_gives_unsettled(self):
        fake = FakeGitHub([("completed", "failure")])
        self.assertEqual(self.run_main(fake), f"copilot=unsettled\nsha={SHA}\n")
        self.assertEqual(len(fake.comments), 1)
        self.assertIn(f"<!-- repair-copilot:{SHA} -->", fake.comments[0])
        self.assertIn(f"https://github.com/o/r/actions/runs/{RUN}", fake.comments[0])
        self.assertIn("`failure`", fake.comments[0])
        self.assertIn(("issue", "comment", "5", "-R", "o/r", "--body-file", "-"), fake.calls)

        self.assertEqual(self.run_main(fake), f"copilot=unsettled\nsha={SHA}\n")
        self.assertEqual(len(fake.comments), 1)

    def test_a_failed_run_on_a_pull_request_that_closes_no_issue_comments_on_the_pull_request(self):
        fake = FakeGitHub([("completed", "cancelled")], body="Part of #5")
        self.run_main(fake)
        self.assertIn(("pr", "comment", "7", "-R", "o/r", "--body-file", "-"), fake.calls)

    def test_a_run_in_progress_past_the_timeout_exits_1(self):
        fake = FakeGitHub([("in_progress", "")])
        with self.assertRaises(SystemExit) as stop:
            self.run_main(fake, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "0"})
        self.assertIsInstance(stop.exception.code, str)
        self.assertEqual(fake.comments, [])

    def test_a_closed_or_unlabeled_pull_request_writes_nothing_and_reads_no_run(self):
        for state, labels in (("CLOSED", ("repair",)), ("MERGED", ("repair",)), ("OPEN", ("bug",))):
            with self.subTest(state=state, labels=labels):
                fake = FakeGitHub([("completed", "success")], state=state, labels=labels)
                self.assertEqual(self.run_main(fake), "")
                self.assertFalse([call for call in fake.calls if call[0] == "run"])

    def test_factory_run_unset_exits_1(self):
        for env in ({"FACTORY_RUN": ""}, {}):
            with self.subTest(env=env):
                fake = FakeGitHub([("completed", "success")])
                values = {"GITHUB_REPOSITORY": "o/r", "GITHUB_OUTPUT": os.devnull, **env}
                with (
                    mock.patch.dict(os.environ, values, clear=True),
                    mock.patch.object(copilot.sys, "argv", ["copilot.py", "7"]),
                    mock.patch.object(copilot, "gh", side_effect=fake),
                    self.assertRaises(SystemExit) as stop,
                ):
                    copilot.main()
                self.assertIsInstance(stop.exception.code, str)
                self.assertFalse([call for call in fake.calls if call[0] == "run"])

    def test_a_closed_pull_request_with_factory_run_unset_writes_nothing(self):
        fake = FakeGitHub([("completed", "success")], state="CLOSED")
        self.assertEqual(self.run_main(fake, {"FACTORY_RUN": ""}), "")

    def test_the_wait_ends_at_the_deadline(self):
        fake = FakeGitHub([("in_progress", "")])
        clock = [1000.0]
        slept = []

        def sleep(seconds):
            slept.append(seconds)
            clock[0] += seconds

        with (
            mock.patch.object(copilot.time, "time", side_effect=lambda: clock[0]),
            mock.patch.object(copilot.time, "sleep", side_effect=sleep),
            self.assertRaises(SystemExit),
        ):
            self.run_main(fake, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "5", "REPAIR_POLL_SECONDS": "3600"})
        self.assertEqual(slept, [5])


if __name__ == "__main__":
    unittest.main()
