"""Unit tests of the token of the ref calls in build.py, decided in #403. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls GitHub: each one replaces
subprocess.run and reads the env that each call gets."""

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

    def test_the_deletion_sends_the_ref_token_as_gh_token(self):
        for value, expected in (("app", "app"), ("", "job")):
            with (
                self.subTest(ref_token=value),
                mock.patch.dict(os.environ, {"GH_TOKEN": "job", "REPAIR_REF_TOKEN": value}),
                mock.patch.object(
                    build.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "", "")
                ) as run,
            ):
                build.delete_base_branch(REPO, BRANCH)
                self.assertEqual(run.call_args.kwargs["env"]["GH_TOKEN"], expected)


if __name__ == "__main__":
    unittest.main()
