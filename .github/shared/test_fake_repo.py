"""Unit tests of fake_repo.py, for #555: FakeRepo resolves each call against Repo and gives its scripted outputs. The
build job of ci.yml runs them with `python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`."""

import unittest
from unittest import mock

from fake_repo import Fail, FakeRepo
from github import GitHubError


class ResolveTests(unittest.TestCase):
    def test_a_name_that_repo_lacks_raises_attribute_error(self):
        with self.assertRaises(AttributeError):
            FakeRepo().pull_requests(7)
        with self.assertRaises(AttributeError):
            FakeRepo().answer("pull_requests", (7,), {})

    def test_a_wrong_argument_list_raises_type_error(self):
        repo = FakeRepo()
        repo.answer("issue", (5,), {})
        for call in (lambda: repo.issue(), lambda: repo.issue(5, 6), lambda: repo.issue(count=5)):
            with self.subTest(call=call):
                with self.assertRaises(TypeError):
                    call()
        with self.assertRaises(TypeError):
            repo.answer("issue", (5, 6), {})

    def test_a_call_by_keyword_takes_the_answer_of_its_positional_form(self):
        repo = FakeRepo()
        repo.answer("comments", (None, "abc"), [{"body": "x"}])
        self.assertEqual(repo.comments(None, sha="abc"), [{"body": "x"}])
        self.assertEqual(repo.calls_of("comments"), [{"number": None, "sha": "abc"}])

    def test_the_name_is_the_repo_name(self):
        self.assertEqual(FakeRepo("a/b").name, "a/b")


class OutputTests(unittest.TestCase):
    def test_a_sequence_gives_its_outputs_in_order_and_the_last_answers_each_later_call(self):
        repo = FakeRepo()
        repo.answer("rest_pull_request", (7,), {"n": 1}, {"n": 2})
        self.assertEqual([repo.rest_pull_request(7)["n"] for _ in range(3)], [1, 2, 2])

    def test_a_fail_raises_github_error(self):
        repo = FakeRepo()
        repo.answer("commit", ("abc",), Fail(status=404))
        with self.assertRaises(GitHubError) as raised:
            repo.commit("abc")
        self.assertEqual(raised.exception.status, 404)

    def test_a_function_gives_its_output_at_the_call(self):
        repo = FakeRepo()
        bodies = []
        repo.answer("comments", (5,), lambda: [{"body": body} for body in bodies])
        bodies.append("later")
        self.assertEqual(repo.comments(5), [{"body": "later"}])

    def test_a_call_with_no_answer_fails(self):
        repo = FakeRepo()
        repo.answer("issue", (5,), {})
        with self.assertRaises(AssertionError):
            repo.issue(6)

    def test_any_matches_each_value_and_the_latest_answer_wins(self):
        repo = FakeRepo()
        repo.answer("post_comment", (7, mock.ANY), None)
        repo.answer("post_comment", (7, "special"), "special")
        self.assertIsNone(repo.post_comment(7, "any text"))
        self.assertEqual(repo.post_comment(7, "special"), "special")

    def test_a_write_changes_no_answer(self):
        repo = FakeRepo()
        repo.answer("comments", (5,), [])
        repo.answer("post_comment", (5, mock.ANY), None)
        repo.post_comment(5, "The text.")
        self.assertEqual(repo.comments(5), [])

    def test_each_call_is_recorded_in_order(self):
        repo = FakeRepo()
        repo.answer("comments", (5,), [])
        repo.answer("post_comment", (5, mock.ANY), None)
        repo.comments(5)
        repo.post_comment(5, "The text.")
        self.assertEqual(
            repo.calls,
            [
                ("comments", {"number": 5, "sha": None}),
                ("post_comment", {"number": 5, "body": "The text.", "sha": None}),
            ],
        )
        self.assertEqual(repo.calls_of("post_comment"), [{"number": 5, "body": "The text.", "sha": None}])


if __name__ == "__main__":
    unittest.main()
