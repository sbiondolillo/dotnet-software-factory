"""Unit tests of closing.py, for #556. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`."""

import unittest

import closing
from fake_github import closes


class IssueTests(unittest.TestCase):
    def test_the_first_issue_of_the_repo_is_the_closing_issue(self):
        self.assertEqual(closing.issue(closes("o/r", 5, 6), "o/r"), 5)

    def test_an_issue_of_another_repo_is_skipped(self):
        pr = {closing.FIELD: closes("o/other", 3)[closing.FIELD] + closes("o/r", 5)[closing.FIELD]}
        self.assertEqual(closing.issue(pr, "o/r"), 5)

    def test_the_repo_matches_in_any_case(self):
        self.assertEqual(closing.issue(closes("O/R", 5), "o/r"), 5)

    def test_an_empty_or_absent_field_closes_no_issue(self):
        for pr in ({closing.FIELD: []}, {closing.FIELD: None}, {}):
            with self.subTest(pr=pr):
                self.assertIsNone(closing.issue(pr, "o/r"))

    def test_only_issues_of_another_repo_close_no_issue(self):
        self.assertIsNone(closing.issue(closes("o/other", 3), "o/r"))


if __name__ == "__main__":
    unittest.main()
