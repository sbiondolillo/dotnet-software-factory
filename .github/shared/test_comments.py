"""Unit tests of comments.py, for #517. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A FakeRepo stands in for the repo, so no test
calls GitHub. Which target a number or a SHA names is a fact of repo.py, and test_repo.py tests it."""

import unittest
from unittest import mock

import comments
from fake_repo import Fail, FakeRepo
from github import GitHubError

MARKER = "<!-- step:abc -->"


def fake(*bodies, number=5, sha=None):
    """A repo whose target holds a comment for each body, and that takes a post there."""
    repo = FakeRepo()
    repo.answer("comments", (number, sha), [{"body": body} for body in bodies])
    repo.answer("post_comment", (number, mock.ANY, sha), None)
    return repo


def posted(repo):
    """The body of each comment posted."""
    return [call["body"] for call in repo.calls_of("post_comment")]


class PostOnceTests(unittest.TestCase):
    def test_a_target_with_no_marker_comment_takes_one_post(self):
        repo = fake("an earlier comment", "<!-- step:other -->\nanother key")
        self.assertTrue(comments.post_once(repo, 5, MARKER, "The text."))
        self.assertEqual(posted(repo), [f"{MARKER}\nThe text."])
        self.assertEqual([name for name, _ in repo.calls], ["comments", "post_comment"])

    def test_a_marker_comment_on_the_target_skips_the_post(self):
        repo = fake("first", f"{MARKER}\nThe text.")
        self.assertFalse(comments.post_once(repo, 5, MARKER, "The text again."))
        self.assertEqual(posted(repo), [])

    def test_a_marker_in_the_last_comment_of_many_skips_the_post(self):
        # Repo.comments reads every page, so has reads each item it gives: here the marker is in the 151st.
        repo = fake(*[f"comment {n}" for n in range(150)], f"{MARKER}\nlate")
        self.assertFalse(comments.post_once(repo, 5, MARKER, "The text."))
        self.assertEqual(posted(repo), [])

    def test_a_comment_with_no_body_holds_no_marker(self):
        repo = FakeRepo()
        repo.answer("comments", (5,), [{"body": None}, {}])
        repo.answer("post_comment", (5, mock.ANY), None)
        self.assertTrue(comments.post_once(repo, 5, MARKER, "The text."))

    def test_the_number_and_the_sha_go_to_the_repo_together(self):
        repo = fake(number=None, sha="abc")
        self.assertTrue(comments.post_once(repo, None, MARKER, "The text.", sha="abc"))
        self.assertEqual(repo.calls_of("comments"), [{"number": None, "sha": "abc"}])
        self.assertEqual(
            repo.calls_of("post_comment"), [{"number": None, "body": f"{MARKER}\nThe text.", "sha": "abc"}]
        )

    def test_a_failed_read_posts_nothing(self):
        repo = FakeRepo()
        repo.answer("comments", (5,), Fail(status=502))
        with self.assertRaises(GitHubError):
            comments.post_once(repo, 5, MARKER, "The text.")
        self.assertEqual(posted(repo), [])


class HasTests(unittest.TestCase):
    def test_has_reads_the_marker_anywhere_in_a_body(self):
        self.assertTrue(comments.has(fake(f"text\n{MARKER}"), 5, MARKER))

    def test_has_reads_no_marker_of_another_key(self):
        self.assertFalse(comments.has(fake("<!-- step:abd -->"), 5, MARKER))

    def test_has_reads_the_target_of_the_sha(self):
        self.assertTrue(comments.has(fake(MARKER, number=None, sha="abc"), None, MARKER, sha="abc"))


if __name__ == "__main__":
    unittest.main()
