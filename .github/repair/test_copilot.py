"""Unit tests of copilot.py, the Copilot stage of the repair loop, decided in #509. The build job of ci.yml runs them
with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A FakeRepo of .github/shared/ stands in for
the repo, so no test calls GitHub."""

import os
import tempfile
import unittest
from unittest import mock

import copilot
from fake_github import closes
from fake_repo import FakeRepo

SHA = "a" * 40
LATER = "c" * 40
RUN = "123"


def fake(runs, state="OPEN", labels=("repair",), issues=(5,), heads=(SHA,)):
    """The repo of copilot.py: the pull request with each head in turn, the run's status in turn, and the comments
    of the issue and of the pull request, which hold each comment the step posts."""
    repo = FakeRepo("o/r")
    pr = {"state": state, "labels": [{"name": n} for n in labels], **closes("o/r", *issues)}
    repo.answer("pull_request", (7,), *[{**pr, "headRefOid": head} for head in heads])
    repo.answer("run_status", (RUN,), *[{"status": status, "conclusion": conclusion} for status, conclusion in runs])
    for number in (5, 7):
        repo.answer("comments", (number,), lambda number=number: [{"body": b} for b in posted(repo, number)])
        repo.answer("post_comment", (number, mock.ANY), None)
    return repo


def posted(repo, number=None):
    """The body of each comment the step posted, on <number> or on any target."""
    return [call["body"] for call in repo.calls_of("post_comment") if number is None or call["number"] == number]


class MainTests(unittest.TestCase):
    def run_main(self, repo, env=None):
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
                copilot.main(repo)
            return out.read()

    def test_a_run_that_passes_gives_settled_and_the_head(self):
        repo = fake([("in_progress", ""), ("completed", "success")])
        self.assertEqual(self.run_main(repo), f"copilot=settled\nsha={SHA}\n")
        self.assertEqual(posted(repo), [])

    def test_the_head_is_read_after_the_run_ends(self):
        repo = fake([("in_progress", ""), ("completed", "success")], heads=(SHA, LATER))
        self.assertEqual(self.run_main(repo), f"copilot=settled\nsha={LATER}\n")

    def test_a_run_that_completes_as_the_timeout_passes_is_read(self):
        repo = fake([("completed", "success")])
        self.assertEqual(self.run_main(repo, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "0"}), f"copilot=settled\nsha={SHA}\n")

    def test_a_run_that_fails_posts_one_comment_and_gives_unsettled(self):
        repo = fake([("completed", "failure")])
        self.assertEqual(self.run_main(repo), f"copilot=unsettled\nsha={SHA}\n")
        self.assertEqual(len(posted(repo)), 1)
        self.assertIn(f"repair-copilot:{SHA}", posted(repo)[0].splitlines()[0])
        self.assertIn(f"https://github.com/o/r/actions/runs/{RUN}", posted(repo)[0])
        self.assertIn("`failure`", posted(repo)[0])
        self.assertEqual(len(posted(repo, 5)), 1)

        self.assertEqual(self.run_main(repo), f"copilot=unsettled\nsha={SHA}\n")
        self.assertEqual(len(posted(repo)), 1)

    def test_a_failed_run_on_a_pull_request_that_closes_no_issue_comments_on_the_pull_request(self):
        repo = fake([("completed", "cancelled")], issues=())
        self.run_main(repo)
        self.assertEqual(len(posted(repo, 7)), 1)
        self.assertEqual(posted(repo, 5), [])

    def test_a_run_in_progress_past_the_timeout_exits_1(self):
        repo = fake([("in_progress", "")])
        with self.assertRaises(SystemExit) as stop:
            self.run_main(repo, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "0"})
        self.assertEqual(
            stop.exception.code,
            f"repair: the factory run https://github.com/o/r/actions/runs/{RUN} is in_progress after the timeout",
        )
        self.assertEqual(posted(repo), [])

    def test_a_closed_or_unlabeled_pull_request_writes_nothing_and_reads_no_run(self):
        for state, labels in (("CLOSED", ("repair",)), ("MERGED", ("repair",)), ("OPEN", ("bug",))):
            with self.subTest(state=state, labels=labels):
                repo = fake([("completed", "success")], state=state, labels=labels)
                self.assertEqual(self.run_main(repo), "")
                self.assertEqual(repo.calls_of("run_status"), [])

    def test_factory_run_unset_exits_1(self):
        for env in ({"FACTORY_RUN": ""}, {}):
            with self.subTest(env=env):
                repo = fake([("completed", "success")])
                values = {"GITHUB_REPOSITORY": "o/r", "GITHUB_OUTPUT": os.devnull, **env}
                with (
                    mock.patch.dict(os.environ, values, clear=True),
                    mock.patch.object(copilot.sys, "argv", ["copilot.py", "7"]),
                    self.assertRaises(SystemExit) as stop,
                ):
                    copilot.main(repo)
                self.assertIsInstance(stop.exception.code, str)
                self.assertEqual(repo.calls_of("run_status"), [])

    def test_a_closed_pull_request_with_factory_run_unset_writes_nothing(self):
        repo = fake([("completed", "success")], state="CLOSED")
        self.assertEqual(self.run_main(repo, {"FACTORY_RUN": ""}), "")

    def test_the_wait_ends_at_the_deadline(self):
        repo = fake([("in_progress", "")])
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
            self.run_main(repo, {"REPAIR_COPILOT_TIMEOUT_SECONDS": "5", "REPAIR_POLL_SECONDS": "3600"})
        self.assertEqual(slept, [5])


if __name__ == "__main__":
    unittest.main()
