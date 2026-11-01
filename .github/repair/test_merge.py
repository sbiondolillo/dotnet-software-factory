"""Unit tests of the path rule of merge.py, decided in #406 and its addendum of 2026-11-01. The build job of ci.yml
runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. They use unittest, which the
standard library holds, so the job installs nothing. The tests of the keys read factory.yml of the branch."""

import os
import unittest
from unittest import mock

import merge

with open(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "workflows", "factory.yml"), encoding="utf-8"
) as file:
    FACTORY_TEXT = file.read()


class GuardedTests(unittest.TestCase):
    def test_each_listed_file_is_refused(self):
        for path in (
            ".github/workflows/judge.yml",
            ".github/workflows/ci.yml",
            ".github/workflows/repair.yml",
            ".claude/skills/change/JUDGES.md",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_a_path_under_a_listed_directory_is_refused(self):
        for path in (".github/judge/judge.py", ".github/repair/merge.py", ".github/repair/new.py"):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_every_other_workflow_path_is_refused(self):
        for path in (
            ".github/workflows/x.yml",
            ".github/workflows/relock.yml",
            ".github/workflows/ci.yml.bak",
            ".github/workflows/sub/x.yml",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_an_open_path_passes(self):
        for path in (
            ".github/workflows/factory.yml",
            "factory.yml",
            "src/x.cs",
            ".github/agents/builder.md",
            "sandbox/Dockerfile",
            ".github/judgement/x.py",
            ".claude/skills/change/SKILL.md",
            "docs/.github/repair/merge.py",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [])

    def test_the_refusal_names_every_listed_path_once(self):
        paths = ["src/x.cs", ".github/repair/merge.py", ".github/workflows/judge.yml", ".github/repair/merge.py"]
        self.assertEqual(merge.guarded(paths), [".github/repair/merge.py", ".github/workflows/judge.yml"])


class FactoryKeyTests(unittest.TestCase):
    def edit(self, old, new):
        self.assertEqual(FACTORY_TEXT.count(old), 1)
        return FACTORY_TEXT.replace(old, new)

    def test_the_file_on_the_branch_holds_both_keys(self):
        self.assertEqual(len(merge.key_blocks(FACTORY_TEXT, "on", True)), 1)
        self.assertEqual(merge.key_blocks(FACTORY_TEXT, "permissions", False), [["permissions:", "  checks: read"]])

    def test_an_edit_outside_the_keys_passes(self):
        head = self.edit("# The run name is", "# The name of the run is").replace(
            "    runs-on: ubuntu-latest", "    runs-on: ubuntu-24.04"
        )
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_an_edit_of_a_comment_in_a_key_passes(self):
        head = self.edit("on:\n  issues:\n", "on:\n  # the label path\n  issues:\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_an_added_pull_request_trigger_is_refused(self):
        head = self.edit("on:\n  issues:\n", "on:\n  pull_request:\n  issues:\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["on:"])

    def test_a_trigger_after_a_comment_at_column_0_is_refused(self):
        head = self.edit("on:\n  issues:\n", "on:\n# x\n  pull_request:\n  issues:\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["on:"])

    def test_a_job_level_grant_is_refused(self):
        head = self.edit("  factory:\n", "  factory:\n    permissions:\n      checks: write\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_grant_in_a_flow_mapping_is_refused(self):
        head = FACTORY_TEXT + "  forge: {runs-on: ubuntu-latest, permissions: {checks: write}, steps: []}\n"
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_grant_in_another_spelling_is_refused(self):
        for line in (
            "    permissions\t: write-all\n",
            "    permissions\t: {checks: write}\n",
            '    "permissions":write-all\n',
            "    !!str permissions: write-all\n",
            "    &grant permissions: write-all\n",
        ):
            with self.subTest(line=line):
                head = self.edit("  factory:\n", "  factory:\n" + line)
                self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_trigger_in_another_spelling_is_refused(self):
        for line in ('"on":[pull_request]\n', "on\t: pull_request\n"):
            with self.subTest(line=line):
                self.assertEqual(merge.changed_keys(FACTORY_TEXT, FACTORY_TEXT + line), ["on:"])

    def test_a_key_in_a_hidden_form_is_refused(self):
        for line in (
            '    "perm\\x69ssions": write-all\n',
            "    ? permissions\n    : write-all\n",
            "    *grant : write-all\n",
            "    env: {? x : y}\n",
        ):
            with self.subTest(line=line):
                head = self.edit("  factory:\n", "  factory:\n" + line)
                self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [merge.UNREADABLE])

    def test_the_file_on_the_branch_holds_no_hidden_form(self):
        self.assertEqual(merge.hidden_lines(FACTORY_TEXT), [])

    def test_a_changed_top_level_grant_is_refused(self):
        head = self.edit("  checks: read\n", "  checks: write\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_deleted_file_changes_both_keys(self):
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, None), ["on:", "permissions:"])

    def test_the_refusal_names_the_changed_key(self):
        self.assertEqual(
            merge.refused([".github/workflows/x.yml"], ["on:"]),
            ["`.github/workflows/x.yml`", "the `on:` key of `.github/workflows/factory.yml`"],
        )
        self.assertEqual(
            merge.refused([], ["on:", "permissions:"]),
            ["the `on:` and `permissions:` keys of `.github/workflows/factory.yml`"],
        )
        self.assertEqual(
            merge.refused([], [merge.UNREADABLE]),
            ["a key in a form the job cannot read in `.github/workflows/factory.yml`"],
        )
        self.assertEqual(merge.refused([], []), [])


class FileAtTests(unittest.TestCase):
    def test_an_absent_file_is_none(self):
        with mock.patch.object(merge, "gh", side_effect=SystemExit("judge: gh api failed: gh: Not Found (HTTP 404)")):
            self.assertIsNone(merge.file_at("o/r", merge.FACTORY, "a" * 40))

    def test_any_other_failure_ends_the_job(self):
        with mock.patch.object(merge, "gh", side_effect=SystemExit("judge: gh api failed: HTTP 502")):
            with self.assertRaises(SystemExit):
                merge.file_at("o/r", merge.FACTORY, "a" * 40)


class ChangedFilesTests(unittest.TestCase):
    def test_a_rename_gives_both_paths(self):
        output = '["src/moved.py",".github/repair/merge.py"]\n[".github/workflows/factory.yml",null]\n'
        with mock.patch.object(merge, "gh", return_value=output) as gh:
            count, paths = merge.changed_files("o/r", 7)
        self.assertEqual(count, 2)
        self.assertEqual(paths, ["src/moved.py", ".github/repair/merge.py", ".github/workflows/factory.yml"])
        self.assertEqual(merge.guarded(paths), [".github/repair/merge.py"])
        self.assertIn("--paginate", gh.call_args.args)


if __name__ == "__main__":
    unittest.main()
