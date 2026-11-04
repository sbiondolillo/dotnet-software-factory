"""Unit tests of copilot.py, the Copilot stage of the repair loop, decided in #509. The build job of ci.yml runs them
with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A FakeGitHub of .github/shared/ stands in for
the adapter, so no test calls GitHub."""

import json
import os
import tempfile
import unittest
from unittest import mock

import comments
import copilot
from fake_github import FakeGitHub

SHA = "a" * 40
LATER = "c" * 40
RUN = "123"
PR_VIEW = ("pr", "view", "7", "-R", "o/r", "--json", copilot.FIELDS)
RUN_VIEW = ("run", "view", RUN, "-R", "o/r", "--json", "status,conclusion")


def fake(runs, state="OPEN", labels=("repair",), body="Closes #5", heads=(SHA,)):
    """The GitHub of copilot.py: the pull request with each head in turn, the run's status in turn, and the comments
    of the issue and of the pull request, which hold each comment the step posts."""
    gh = FakeGitHub()
    pr = {"state": state, "labels": [{"name": n} for n in labels], "body": body}
    gh.answer(PR_VIEW, *[{**pr, "headRefOid": head} for head in heads])
    gh.answer(RUN_VIEW, *[{"status": status, "conclusion": conclusion} for status, conclusion in runs])
    for number in ("5", "7"):
        gh.answer_list(comments.path("o/r", number), lambda number=number: [{"body": b} for b in posted(gh, number)])
        gh.answer(post(number), "{}")
    return gh


def post(number):
    """The call of comments.py that posts a comment on issue or pull request <number>."""
    return ("api", "-X", "POST", comments.path("o/r", number), "--input", "-")


def posted(gh, number=None):
    """The body of each comment the step posted, on <number> or on any target."""
    targets = [post(n) for n in ("5", "7")] if number is None else [post(number)]
    return [json.loads(text)["body"] for args, text in gh.calls if args in targets]


class MainTests(unittest.TestCase):
    def run_main(self, gh, env=None):
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
            ):
                copilot.main(gh)
            return out.read()

    def test_a_run_that_passes_gives_settled_and_the_head(self):
        gh = fake([("in_progress", ""), ("completed", "success")])
        self.assertEqual(self.run_main(gh), f"copilot=settled\nsha={SHA}\n")
        self.assertEqual(posted(gh), [])

    def test_the_head_is_read_after_the_run_ends(self):
        gh = fake([("in_progress", ""), ("completed", "success")], heads=(SHA, LATER))
        self.assertEqual(self.run_main(gh), f"copilot=settled\nsha={LATER}\n")

    def test_a_run_that_completes_as_the_timeout_passes_is_read(self):
        gh = fake([("completed", "success")])
        self.assertEqual(self.run_main(gh, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "0"}), f"copilot=settled\nsha={SHA}\n")

    def test_a_run_that_fails_posts_one_comment_and_gives_unsettled(self):
        gh = fake([("completed", "failure")])
        self.assertEqual(self.run_main(gh), f"copilot=unsettled\nsha={SHA}\n")
        self.assertEqual(len(posted(gh)), 1)
        self.assertIn(f"repair-copilot:{SHA}", posted(gh)[0].splitlines()[0])
        self.assertIn(f"https://github.com/o/r/actions/runs/{RUN}", posted(gh)[0])
        self.assertIn("`failure`", posted(gh)[0])
        self.assertEqual(len(posted(gh, "5")), 1)

        self.assertEqual(self.run_main(gh), f"copilot=unsettled\nsha={SHA}\n")
        self.assertEqual(len(posted(gh)), 1)

    def test_a_failed_run_on_a_pull_request_that_closes_no_issue_comments_on_the_pull_request(self):
        gh = fake([("completed", "cancelled")], body="Part of #5")
        self.run_main(gh)
        self.assertEqual(len(posted(gh, "7")), 1)
        self.assertEqual(posted(gh, "5"), [])

    def test_a_run_in_progress_past_the_timeout_exits_1(self):
        gh = fake([("in_progress", "")])
        with self.assertRaises(SystemExit) as stop:
            self.run_main(gh, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "0"})
        self.assertEqual(
            stop.exception.code,
            f"repair: the factory run https://github.com/o/r/actions/runs/{RUN} is in_progress after the timeout",
        )
        self.assertEqual(posted(gh), [])

    def test_a_closed_or_unlabeled_pull_request_writes_nothing_and_reads_no_run(self):
        for state, labels in (("CLOSED", ("repair",)), ("MERGED", ("repair",)), ("OPEN", ("bug",))):
            with self.subTest(state=state, labels=labels):
                gh = fake([("completed", "success")], state=state, labels=labels)
                self.assertEqual(self.run_main(gh), "")
                self.assertFalse([args for args in gh.args() if args[0] == "run"])

    def test_factory_run_unset_exits_1(self):
        for env in ({"FACTORY_RUN": ""}, {}):
            with self.subTest(env=env):
                gh = fake([("completed", "success")])
                values = {"GITHUB_REPOSITORY": "o/r", "GITHUB_OUTPUT": os.devnull, **env}
                with (
                    mock.patch.dict(os.environ, values, clear=True),
                    mock.patch.object(copilot.sys, "argv", ["copilot.py", "7"]),
                    self.assertRaises(SystemExit) as stop,
                ):
                    copilot.main(gh)
                self.assertIsInstance(stop.exception.code, str)
                self.assertFalse([args for args in gh.args() if args[0] == "run"])

    def test_a_closed_pull_request_with_factory_run_unset_writes_nothing(self):
        gh = fake([("completed", "success")], state="CLOSED")
        self.assertEqual(self.run_main(gh, {"FACTORY_RUN": ""}), "")

    def test_the_wait_ends_at_the_deadline(self):
        gh = fake([("in_progress", "")])
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
            self.run_main(gh, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "5", "REPAIR_POLL_SECONDS": "3600"})
        self.assertEqual(slept, [5])


if __name__ == "__main__":
    unittest.main()
