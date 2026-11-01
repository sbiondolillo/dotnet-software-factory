"""Unit tests of the path rule of merge.py, decided in #406. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. They use unittest, which the standard library holds,
so the job installs nothing."""

import unittest
from unittest import mock

import merge


class GuardedTests(unittest.TestCase):
    def test_each_listed_file_is_refused(self):
        for path in (".github/workflows/judge.yml", ".github/workflows/ci.yml", ".github/workflows/repair.yml"):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_a_path_under_a_listed_directory_is_refused(self):
        for path in (".github/judge/judge.py", ".github/repair/merge.py", ".github/repair/new.py"):
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
            ".github/workflows/ci.yml.bak",
            "docs/.github/repair/merge.py",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [])

    def test_the_refusal_names_every_listed_path_once(self):
        paths = ["src/x.cs", ".github/repair/merge.py", ".github/workflows/judge.yml", ".github/repair/merge.py"]
        self.assertEqual(merge.guarded(paths), [".github/repair/merge.py", ".github/workflows/judge.yml"])


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
