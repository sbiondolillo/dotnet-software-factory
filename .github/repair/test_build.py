"""Unit tests of build.py: the token of the create, the move and the deletion of repair-base/<n>, decided in #403,
and the run that the dispatch gives back, from #469. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls GitHub: each one replaces
subprocess.run or gh."""

import json
import os
import subprocess
import unittest
from unittest import mock

import build

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


if __name__ == "__main__":
    unittest.main()
