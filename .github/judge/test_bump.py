"""Unit tests of the automatic path for a pure Dependabot bump, for #448: the four conditions of bump.py, and the
branch of judge.py that takes it. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/judge -p 'test_*.py'`. No test calls the API. The pass case is the data
of pull request #432, head e828943, trimmed to the fields that bump.py reads. The repo is a FakeRepo of
.github/shared/fake_repo.py."""

import copy
import os
import unittest
from unittest import mock

import bump
import judge
from fake_repo import FakeRepo

FIRST = "327b025692914836179a102b3f03db51a6f10b2b"
RELOCK = "e828943ed9d3c9cd2a65b2d629c70787c4a3ecf0"

PROPS_PATCH = """@@ -6,14 +6,14 @@
   </PropertyGroup>
   <ItemGroup>
     <PackageVersion Include="Gherkin" Version="35.1.0" />
-    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.13" />
+    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.14" />
     <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.11.0" />
     <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.11.0" />
     <PackageVersion Include="Moq" Version="4.21.0" />
     <PackageVersion Include="Moq.Analyzers" Version="0.4.2" />
     <PackageVersion Include="Reqnroll" Version="3.3.4" />
     <PackageVersion Include="Reqnroll.xunit.v3" Version="3.3.4" />
-    <PackageVersion Include="System.CommandLine" Version="2.0.13" />
+    <PackageVersion Include="System.CommandLine" Version="2.0.14" />
     <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
     <PackageVersion Include="xunit.v3" Version="3.2.2" />
   </ItemGroup>"""

LOCK_PATCH = """@@ -38,9 +38,9 @@
       "System.CommandLine": {
         "type": "Direct",
-        "requested": "[2.0.13, )",
-        "resolved": "2.0.13",
+        "requested": "[2.0.14, )",
+        "resolved": "2.0.14",
       },"""

LOCKS = ["src/App/packages.lock.json", "src/Core/packages.lock.json", "tests/Integration/packages.lock.json"]
LOCKS += ["tests/Specs/packages.lock.json"]

PR = {
    "number": 432,
    "user": {"login": "dependabot[bot]"},
    "head": {"sha": RELOCK},
    "commits": 2,
    "changed_files": 5,
}

COMMITS = [
    {
        "sha": FIRST,
        "author": {"login": "dependabot[bot]"},
        "commit": {
            "message": "Bump the minor-and-patch group with 2 updates\n\nBumps Microsoft.Extensions.Hosting",
            "verification": {"verified": True},
        },
    },
    {
        "sha": RELOCK,
        "author": {"login": "github-actions[bot]"},
        "commit": {"message": "Regenerate the lock files\n\n[dependabot skip]", "verification": {"verified": False}},
    },
]

LOCK_CHANGES = [158, 3, 4, 4]

DETAILS = {
    RELOCK: {
        "files": [
            {"filename": path, "status": "modified", "additions": n, "deletions": n}
            for path, n in zip(LOCKS, LOCK_CHANGES)
        ],
        "stats": {"additions": 169, "deletions": 169, "total": 338},
    }
}

FILES = [
    {"filename": "Directory.Packages.props", "status": "modified", "additions": 2, "deletions": 2, "patch": PROPS_PATCH}
]
FILES += [
    {"filename": path, "status": "modified", "additions": n, "deletions": n, "patch": LOCK_PATCH}
    for path, n in zip(LOCKS, LOCK_CHANGES)
]

PACKAGES = [("Microsoft.Extensions.Hosting", "10.0.13", "10.0.14"), ("System.CommandLine", "2.0.13", "2.0.14")]


def set_patch(file, patch):
    """Gives the file the patch, with `additions` and `deletions` that count its lines, as GitHub does."""
    lines = patch.split("\n")
    file.update(
        patch=patch,
        additions=sum(line.startswith("+") for line in lines),
        deletions=sum(line.startswith("-") for line in lines),
    )


def props_patch(old, new):
    return f"@@ -1,3 +1,3 @@\n   <ItemGroup>\n-{old}\n+{new}\n   </ItemGroup>"


class PureBumpTests(unittest.TestCase):
    def setUp(self):
        self.pr = copy.deepcopy(PR)
        self.commits = copy.deepcopy(COMMITS)
        self.details = copy.deepcopy(DETAILS)
        self.files = copy.deepcopy(FILES)

    def failed(self):
        return bump.failures(self.pr, self.commits, self.details, self.files)[0]

    def assertFails(self, condition):
        failed = self.failed()
        self.assertTrue(failed, "a pure bump")
        self.assertTrue(all(line.startswith(f"{condition}: ") for line in failed), failed)

    def test_pull_request_432_is_a_pure_bump(self):
        failed, packages = bump.failures(PR, COMMITS, DETAILS, FILES)
        self.assertEqual(failed, [])
        self.assertEqual(packages, PACKAGES)

    def test_the_summary_lists_each_package_with_its_old_and_new_version(self):
        self.assertEqual(
            bump.summary(PACKAGES),
            "- Microsoft.Extensions.Hosting: 10.0.13 to 10.0.14\n- System.CommandLine: 2.0.13 to 2.0.14",
        )

    # Condition 1

    def test_an_author_other_than_dependabot_fails_1(self):
        self.pr["user"]["login"] = "sbiondolillo"
        self.assertFails(1)

    def test_a_missing_author_fails_1(self):
        del self.pr["user"]
        self.assertFails(1)

    # Condition 2

    def test_a_first_commit_by_another_account_fails_2(self):
        self.commits[0]["author"]["login"] = "sbiondolillo"
        self.assertFails(2)

    def test_a_first_commit_with_no_account_fails_2(self):
        self.commits[0]["author"] = None
        self.assertFails(2)

    def test_an_unverified_first_commit_fails_2(self):
        self.commits[0]["commit"]["verification"]["verified"] = False
        self.assertFails(2)

    def test_a_first_commit_with_no_verification_fails_2(self):
        del self.commits[0]["commit"]["verification"]
        self.assertFails(2)

    def test_a_later_commit_by_another_account_fails_2(self):
        self.commits[1]["author"]["login"] = "sbiondolillo"
        self.assertFails(2)

    def test_a_later_commit_with_another_message_fails_2(self):
        self.commits[1]["commit"]["message"] = "Regenerate the lock files, and more"
        self.assertFails(2)

    def test_a_later_commit_that_changes_another_file_fails_2(self):
        props = {"filename": "Directory.Packages.props", "status": "modified", "additions": 0, "deletions": 0}
        self.details[RELOCK]["files"].append(props)
        self.assertFails(2)

    def test_a_later_commit_whose_files_are_not_read_fails_2(self):
        del self.details[RELOCK]
        self.assertFails(2)

    def test_a_later_commit_whose_files_fall_short_of_its_stats_fails_2(self):
        self.details[RELOCK]["files"].pop()
        self.assertFails(2)

    def test_a_later_commit_with_no_stats_fails_2(self):
        del self.details[RELOCK]["stats"]
        self.assertFails(2)

    def test_a_list_of_commits_shorter_than_the_pull_request_fails_2(self):
        self.pr["commits"] = 3
        self.assertFails(2)

    def test_a_pull_request_with_no_commit_count_fails_2(self):
        del self.pr["commits"]
        self.assertFails(2)

    def test_no_commit_fails_2(self):
        self.commits = []
        self.assertFails(2)

    # Condition 3

    def test_an_added_file_fails_3(self):
        self.files[1]["status"] = "added"
        self.assertFails(3)

    def test_a_renamed_file_fails_3(self):
        self.files[1]["previous_filename"] = "src/Old/packages.lock.json"
        self.assertFails(3)

    def test_another_path_fails_3(self):
        self.files.append({"filename": "src/Core/Git.cs", "status": "modified", "patch": "@@ -1 +1 @@\n-a\n+b"})
        self.pr["changed_files"] = 6
        self.assertFails(3)

    def test_a_path_that_only_ends_in_the_lock_name_fails_3(self):
        self.files[1]["filename"] = "src/App/notpackages.lock.json"
        self.assertFails(3)

    def test_a_props_file_below_the_root_fails_3(self):
        self.files.append({"filename": "src/Directory.Packages.props", "status": "modified", "patch": PROPS_PATCH})
        self.pr["changed_files"] = 6
        self.assertFails(3)

    def test_a_list_of_files_shorter_than_changed_files_fails_3(self):
        self.pr["changed_files"] = 6
        self.assertFails(3)

    # Condition 4

    def test_a_patch_cut_short_of_the_additions_and_deletions_fails_4(self):
        self.files[0]["additions"] = 3
        self.files[0]["deletions"] = 3
        self.assertFails(4)

    def test_a_missing_patch_fails_4(self):
        del self.files[0]["patch"]
        self.assertFails(4)

    def test_no_props_file_fails_4(self):
        self.files = self.files[1:]
        self.pr["changed_files"] = 4
        self.assertFails(4)

    def test_a_prerelease_version_fails_4(self):
        set_patch(
            self.files[0],
            props_patch(
                '    <PackageVersion Include="Moq" Version="4.21.0" />',
                '    <PackageVersion Include="Moq" Version="4.22.0-beta.1" />',
            ),
        )
        self.assertFails(4)

    def test_a_new_major_version_fails_4(self):
        set_patch(
            self.files[0],
            props_patch(
                '    <PackageVersion Include="Moq" Version="4.21.0" />',
                '    <PackageVersion Include="Moq" Version="5.0.0" />',
            ),
        )
        self.assertFails(4)

    def test_another_name_fails_4(self):
        set_patch(
            self.files[0],
            props_patch(
                '    <PackageVersion Include="Moq" Version="4.21.0" />',
                '    <PackageVersion Include="Moq2" Version="4.21.1" />',
            ),
        )
        self.assertFails(4)

    def test_another_indent_fails_4(self):
        set_patch(
            self.files[0],
            props_patch(
                '    <PackageVersion Include="Moq" Version="4.21.0" />',
                '  <PackageVersion Include="Moq" Version="4.21.1" />',
            ),
        )
        self.assertFails(4)

    def test_a_line_that_is_not_a_package_version_fails_4(self):
        set_patch(
            self.files[0],
            props_patch(
                "    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>",
                "    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>",
            ),
        )
        self.assertFails(4)

    def test_an_added_line_with_no_removed_line_fails_4(self):
        set_patch(
            self.files[0],
            '@@ -1,2 +1,3 @@\n   <ItemGroup>\n+    <PackageVersion Include="Moq" Version="4.21.0" />',
        )
        self.assertFails(4)

    def test_an_extra_attribute_fails_4(self):
        set_patch(
            self.files[0],
            props_patch(
                '    <PackageVersion Include="Moq" Version="4.21.0" />',
                '    <PackageVersion Include="Moq" Version="4.21.1" Condition="true" />',
            ),
        )
        self.assertFails(4)


REPO = "o/r"
POST = {
    "name": "judge-verdict",
    "sha": RELOCK,
    "conclusion": "success",
    "title": bump.TITLE,
    "summary": bump.summary(PACKAGES),
}


def full_judge():
    raise RuntimeError("the full judge")


def fake(pr=PR, heads=None, files=FILES):
    """The repo of #432. `heads` gives the head of each read of the pull request in turn. A read of the pull request
    for the full judge raises RuntimeError."""
    repo = FakeRepo(REPO)
    repo.answer("rest_pull_request", (432,), *[{**pr, "head": {"sha": head}} for head in heads or [RELOCK]])
    repo.answer("commits", (432,), COMMITS)
    repo.answer("files", (432,), files)
    repo.answer("commit", (RELOCK,), DETAILS[RELOCK])
    repo.answer("post_check", tuple(POST.values()), None)
    repo.answer("pull_request", (432,), full_judge)
    return repo


class AutomaticPathTests(unittest.TestCase):
    def run_main(self, event, author, extra=None):
        repo = fake({**PR, "user": {"login": author}})
        variables = {"GITHUB_REPOSITORY": REPO, "GITHUB_EVENT_NAME": event, **(extra or {})}
        with mock.patch.dict(os.environ, variables, clear=True), mock.patch("sys.argv", ["judge.py", "432"]):
            try:
                judge.main(repo)
            except RuntimeError:
                pass
        return repo

    def test_a_dependabot_pull_request_event_posts_the_verdict_with_no_judge_variable(self):
        repo = self.run_main("pull_request", "dependabot[bot]")
        self.assertEqual(repo.calls_of("pull_request"), [])
        self.assertEqual(repo.calls_of("post_check"), [POST])

    def test_workflow_dispatch_on_a_dependabot_pull_request_runs_the_full_judge(self):
        judge_variables = {"JUDGE_AGENT_ID": "a", "JUDGE_ENVIRONMENT_ID": "e"}
        repo = self.run_main("workflow_dispatch", "dependabot[bot]", judge_variables)
        self.assertEqual(repo.calls, [("pull_request", {"number": 432})])

    def test_an_owner_pull_request_event_runs_the_full_judge(self):
        judge_variables = {"JUDGE_AGENT_ID": "a", "JUDGE_ENVIRONMENT_ID": "e"}
        repo = self.run_main("pull_request", "sbiondolillo", judge_variables)
        self.assertEqual(len(repo.calls_of("pull_request")), 1)
        self.assertEqual(repo.calls_of("post_check"), [])

    def test_a_head_that_moved_while_the_job_read_it_posts_nothing(self):
        repo = fake(heads=["f" * 40])
        judge.judge_bump(repo, 432, {**PR})
        self.assertEqual(repo.calls_of("post_check"), [])

    def test_a_dependabot_pull_request_that_is_not_a_pure_bump_posts_nothing(self):
        files = copy.deepcopy(FILES)
        set_patch(
            files[0],
            props_patch(
                '    <PackageVersion Include="Moq" Version="4.21.0" />',
                '    <PackageVersion Include="Moq" Version="5.0.0" />',
            ),
        )
        repo = fake(files=files)
        with mock.patch("builtins.print") as printed:
            judge.judge_bump(repo, 432, {**PR})
        self.assertEqual(repo.calls_of("post_check"), [])
        self.assertTrue(any("failed 4:" in str(call) for call in printed.call_args_list))


if __name__ == "__main__":
    unittest.main()
