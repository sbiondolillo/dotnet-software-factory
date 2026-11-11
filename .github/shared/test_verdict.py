"""Unit tests of verdict.py, for #556. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A FakeRepo stands in for the repo, so no test
calls GitHub."""

import unittest
from unittest import mock

import verdict
from fake_repo import FakeRepo

SHA = "a" * 40


class PostTests(unittest.TestCase):
    def test_the_post_is_a_check_run_named_judge_verdict_on_the_commit(self):
        repo = FakeRepo()
        repo.answer("post_check", (mock.ANY,) * 5, None)
        verdict.post(repo, SHA, "failure", "CHANGES REQUESTED", "2 angles, 1 findings")
        self.assertEqual(
            repo.calls_of("post_check"),
            [
                {
                    "name": "judge-verdict",
                    "sha": SHA,
                    "conclusion": "failure",
                    "title": "CHANGES REQUESTED",
                    "summary": "2 angles, 1 findings",
                }
            ],
        )


class LatestTests(unittest.TestCase):
    def test_latest_reads_the_check_run_named_judge_verdict_on_the_commit(self):
        repo = FakeRepo()
        repo.answer("latest_check", ("judge-verdict", SHA), ("completed", "success"))
        self.assertEqual(verdict.latest(repo, SHA), ("completed", "success"))

    def test_no_check_run_is_none(self):
        repo = FakeRepo()
        repo.answer("latest_check", ("judge-verdict", SHA), None)
        self.assertIsNone(verdict.latest(repo, SHA))


if __name__ == "__main__":
    unittest.main()
