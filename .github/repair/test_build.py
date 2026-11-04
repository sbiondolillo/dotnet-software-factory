"""Unit tests of build.py: the token of the create, the move and the deletion of repair-base/<n>, decided in #403,
the run that the dispatch gives back, from #469, and the read of the traveler of the issue, from #513. The build job
of ci.yml runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls GitHub:
each one replaces subprocess.run or gh."""

import json
import os
import subprocess
import unittest
from unittest import mock

import build
import traveler

REPO = "owner/repo"
BRANCH = "repair-base/1"
SHA = "0" * 40


def answers(read):
    """The answers of the ref read, then of the write that follows it."""
    return [
        subprocess.CompletedProcess([], read[0], "", read[1]),
        subprocess.CompletedProcess([], 0, "", ""),
    ]


READS = {
    "a missing branch gets a POST": (1, "gh: Not Found (HTTP 404)"),
    "a branch left by an earlier run gets a PATCH": (0, ""),
}


class RefTokenTests(unittest.TestCase):
    def run_create(self, read):
        with mock.patch.object(build.subprocess, "run", side_effect=answers(read)) as run:
            build.create_base_branch(REPO, BRANCH, SHA)
        self.assertEqual(run.call_count, 2)
        return [call.kwargs["env"] for call in run.call_args_list]

    def test_the_ref_calls_send_the_ref_token_as_gh_token(self):
        for name, read in READS.items():
            with self.subTest(name), mock.patch.dict(os.environ, {"GH_TOKEN": "job", "REPAIR_REF_TOKEN": "app"}):
                for env in self.run_create(read):
                    self.assertEqual(env["GH_TOKEN"], "app")
                    self.assertEqual(env["REPAIR_REF_TOKEN"], "app")

    def test_with_no_ref_token_the_ref_calls_get_the_process_environment(self):
        for value in (None, ""):
            for name, read in READS.items():
                with self.subTest(name, ref_token=value), mock.patch.dict(os.environ, {"GH_TOKEN": "job"}):
                    if value is None:
                        os.environ.pop("REPAIR_REF_TOKEN", None)
                    else:
                        os.environ["REPAIR_REF_TOKEN"] = value
                    for env in self.run_create(read):
                        self.assertEqual(env, dict(os.environ))
                        self.assertEqual(env["GH_TOKEN"], "job")

    def test_the_deletion_uses_the_job_token_with_or_without_a_ref_token(self):
        for value in ("app", "", None):
            with (
                self.subTest(ref_token=value),
                mock.patch.dict(os.environ, {"GH_TOKEN": "job"}),
                mock.patch.object(
                    build.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "", "")
                ) as run,
            ):
                if value is None:
                    os.environ.pop("REPAIR_REF_TOKEN", None)
                else:
                    os.environ["REPAIR_REF_TOKEN"] = value
                build.delete_base_branch(REPO, BRANCH)
                self.assertNotIn("env", run.call_args.kwargs)
                self.assertEqual(os.environ["GH_TOKEN"], "job")


class DispatchTests(unittest.TestCase):
    def test_the_dispatch_gives_back_the_run_of_its_issue_when_another_issue_comes_first(self):
        other = {"databaseId": 2, "displayTitle": "#999", "url": "https://example/2"}
        own = {"databaseId": 3, "displayTitle": "#7", "url": "https://example/3"}
        old = {"databaseId": 1, "displayTitle": "#7", "url": "https://example/1"}
        lists = iter([[old], [other, own, old]])

        def gh(*args, **kwargs):
            """A run list answers with the fields of --json alone, as gh does, and any other call with no output."""
            if args[:2] != ("run", "list"):
                return ""
            fields = args[args.index("--json") + 1].split(",")
            return json.dumps([{key: run[key] for key in fields if key in run} for run in next(lists)])

        with mock.patch.object(build, "gh", side_effect=gh), mock.patch.object(build.time, "sleep"):
            run = build.dispatch(REPO, 7, 180)
        self.assertEqual(run, own)


class TravelerTests(unittest.TestCase):
    def issue(self, **fields):
        ticket = traveler.Traveler(
            run="https://github.com/o/r/actions/runs/9", target="`fix/x`", sha=SHA, session="sesn_1", **fields
        )
        return {
            "number": 7,
            "state": "OPEN",
            "labels": [{"name": "repair"}],
            "comments": [],
            "body": traveler.render(ticket),
        }

    def run_main(self, issue):
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            mock.patch.object(build, "read_issue", return_value=issue),
            mock.patch.object(build, "gh", return_value="") as gh,
            mock.patch.object(build, "build") as built,
        ):
            build.main()
        return gh, built

    def test_an_issue_whose_traveler_targets_a_pull_request_gets_one_comment_and_no_build(self):
        gh, built = self.run_main(self.issue(pull_request=3))
        built.assert_not_called()
        gh.assert_called_once()
        self.assertIn("the fault is on the branch of pull request #3", gh.call_args.args[-1])

    def test_an_issue_whose_traveler_targets_main_is_built(self):
        gh, built = self.run_main(self.issue())
        gh.assert_not_called()
        built.assert_called_once_with(REPO, 7, 3600)

    def test_a_factory_pull_request_closes_its_issue_alone_on_a_line(self):
        for body, expected in (("The fix.\n\nCloses #7\n", True), ("Closes #8\n", False), ("See Closes #7.", False)):
            fields = {"state": "OPEN", "headRefName": "factory/1", "body": body}
            with self.subTest(body=body), mock.patch.object(build, "gh", return_value=json.dumps(fields)):
                self.assertIs(build.is_factory_pull_request(REPO, 5, 7), expected)


if __name__ == "__main__":
    unittest.main()
