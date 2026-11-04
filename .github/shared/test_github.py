"""Unit tests of github.py and fake_github.py, for #515. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A fake runner stands in for subprocess.run, so no
test runs gh."""

import json
import os
import subprocess
import unittest
from unittest import mock

from fake_github import Fail, FakeGitHub
from github import GitHub, GitHubError


class Runner:
    """subprocess.run for gh: each call takes the next answer, as (exit code, stdout, stderr), and is recorded."""

    def __init__(self, *answers):
        self.answers = list(answers)
        self.calls = []

    def __call__(self, command, **kwargs):
        self.calls.append((command, kwargs))
        code, stdout, stderr = self.answers.pop(0)
        return subprocess.CompletedProcess(command, code, stdout, stderr)


class RunTests(unittest.TestCase):
    def test_exit_0_gives_the_standard_output(self):
        runner = Runner((0, "out\n", "warning"))
        self.assertEqual(GitHub("t", runner).run("api", "repos/o/r"), "out\n")
        self.assertEqual(runner.calls[0][0], ["gh", "api", "repos/o/r"])

    def test_the_input_text_reaches_standard_input(self):
        runner = Runner((0, "", ""))
        GitHub("t", runner).run("pr", "comment", "7", "--body-file", "-", input_text="the body")
        self.assertEqual(runner.calls[0][1]["input"], "the body")

    def test_an_http_failure_raises_with_the_status(self):
        runner = Runner((1, "", "gh: Not Found (HTTP 404)\n"))
        with self.assertRaises(GitHubError) as raised:
            GitHub("t", runner).run("api", "repos/o/r/git/ref/heads/x")
        self.assertEqual(raised.exception.status, 404)
        self.assertEqual(raised.exception.returncode, 1)
        self.assertEqual(raised.exception.stderr, "gh: Not Found (HTTP 404)\n")
        self.assertIn("gh api repos/o/r/git/ref/heads/x failed: gh: Not Found (HTTP 404)", str(raised.exception))

    def test_a_failure_with_no_status_raises_with_status_none(self):
        runner = Runner((1, "", "could not resolve to a PullRequest"))
        with self.assertRaises(GitHubError) as raised:
            GitHub("t", runner).run("pr", "view", "7")
        self.assertIsNone(raised.exception.status)

    def test_a_failure_carries_the_standard_output(self):
        runner = Runner((1, "the log of the other jobs\n", "log not found"))
        with self.assertRaises(GitHubError) as raised:
            GitHub("t", runner).run("run", "view", "9", "--log-failed")
        self.assertEqual(raised.exception.stdout, "the log of the other jobs\n")
        self.assertEqual(raised.exception.command, ("run", "view", "9", "--log-failed"))

    def test_the_token_of_the_adapter_reaches_the_environment_of_the_runner(self):
        runner = Runner((0, "", ""), (0, "", ""))
        with mock.patch.dict(os.environ, {"GH_TOKEN": "job", "PATH": "/bin"}):
            GitHub("app", runner).run("api", "x")
            GitHub("job", runner).run("api", "x")
        self.assertEqual([kwargs["env"]["GH_TOKEN"] for _, kwargs in runner.calls], ["app", "job"])
        self.assertEqual(runner.calls[0][1]["env"]["PATH"], "/bin")

    def test_from_env_reads_gh_token(self):
        with mock.patch.dict(os.environ, {"GH_TOKEN": "job"}):
            self.assertEqual(GitHub.from_env().token, "job")
        for env in ({"GH_TOKEN": ""}, {}):
            with self.subTest(env=env), mock.patch.dict(os.environ, env, clear=True), self.assertRaises(SystemExit):
                GitHub.from_env()


class ApiListTests(unittest.TestCase):
    def test_the_items_of_every_page_join(self):
        pages = "".join(json.dumps(item) + "\n" for item in ({"n": 1}, {"n": 2}, {"n": 3}))
        runner = Runner((0, pages, ""))
        self.assertEqual(GitHub("t", runner).api_list("repos/o/r/pulls/7/files"), [{"n": 1}, {"n": 2}, {"n": 3}])
        self.assertEqual(
            runner.calls[0][0],
            ["gh", "api", "repos/o/r/pulls/7/files?per_page=100", "--paginate", "--jq", ".[] | @json"],
        )

    def test_an_empty_list_gives_no_item(self):
        self.assertEqual(GitHub("t", Runner((0, "", ""))).api_list("repos/o/r/x"), [])


class FakeTests(unittest.TestCase):
    def test_each_call_takes_the_next_output_and_the_last_one_stays(self):
        fake = FakeGitHub()
        fake.answer(("api", "x"), "a", {"b": 1}, lambda: "c")
        self.assertEqual([fake.run("api", "x") for _ in range(4)], ["a", '{"b": 1}', "c", "c"])
        self.assertEqual(fake.count("api", "x"), 4)

    def test_a_fail_raises_with_its_status_and_output(self):
        fake = FakeGitHub()
        fake.answer(("api", "x"), Fail(status=404, stdout="kept"))
        with self.assertRaises(GitHubError) as raised:
            fake.run("api", "x")
        self.assertEqual((raised.exception.status, raised.exception.stdout), (404, "kept"))

    def test_a_call_with_no_answer_fails_the_test(self):
        with self.assertRaises(AssertionError):
            FakeGitHub().run("api", "y")

    def test_the_calls_and_their_input_are_recorded(self):
        fake = FakeGitHub()
        fake.answer(("pr", "comment", "7"), "")
        fake.answer_list("repos/o/r/x", [{"n": 1}])
        fake.run("pr", "comment", "7", input_text="body")
        self.assertEqual(fake.api_list("repos/o/r/x"), [{"n": 1}])
        self.assertEqual(fake.args(), [("pr", "comment", "7"), ("api_list", "repos/o/r/x")])
        self.assertEqual(fake.input_of("pr", "comment", "7"), "body")


if __name__ == "__main__":
    unittest.main()
