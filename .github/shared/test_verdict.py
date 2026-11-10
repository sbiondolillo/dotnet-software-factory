"""Unit tests of verdict.py, for #556. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A FakeGitHub stands in for the adapter, so no test
calls GitHub."""

import unittest

import verdict
from fake_github import FakeGitHub

SHA = "a" * 40
LATEST = ("api", f"repos/o/r/commits/{SHA}/check-runs?check_name=judge-verdict&filter=latest")


class PostTests(unittest.TestCase):
    def test_the_post_is_a_completed_check_run_named_judge_verdict_on_the_commit(self):
        gh = FakeGitHub()
        post = (
            "api",
            "repos/o/r/check-runs",
            "-f",
            "name=judge-verdict",
            "-f",
            f"head_sha={SHA}",
            "-f",
            "status=completed",
            "-f",
            "conclusion=failure",
            "-f",
            "output[title]=CHANGES REQUESTED",
            "-f",
            "output[summary]=2 angles, 1 findings",
        )
        gh.answer(post, "{}")
        verdict.post(gh, "o/r", SHA, "failure", "CHANGES REQUESTED", "2 angles, 1 findings")
        self.assertEqual(gh.args(), [post])


class LatestTests(unittest.TestCase):
    def test_the_latest_check_run_gives_its_status_and_conclusion(self):
        gh = FakeGitHub()
        gh.answer(LATEST, {"check_runs": [{"status": "completed", "conclusion": "success"}]})
        self.assertEqual(verdict.latest(gh, "o/r", SHA), ("completed", "success"))

    def test_a_check_run_in_progress_has_no_conclusion(self):
        gh = FakeGitHub()
        gh.answer(LATEST, {"check_runs": [{"status": "in_progress"}]})
        self.assertEqual(verdict.latest(gh, "o/r", SHA), ("in_progress", None))

    def test_no_check_run_is_none(self):
        gh = FakeGitHub()
        gh.answer(LATEST, {"check_runs": []})
        self.assertIsNone(verdict.latest(gh, "o/r", SHA))


if __name__ == "__main__":
    unittest.main()
