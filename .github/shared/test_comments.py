"""Unit tests of comments.py, for #517. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A FakeGitHub stands in for the adapter, so no test
calls GitHub."""

import json
import unittest

import comments
from fake_github import Fail, FakeGitHub
from github import GitHubError

MARKER = "<!-- step:abc -->"
ISSUE = "repos/o/r/issues/5/comments"
COMMIT = "repos/o/r/commits/abc/comments"
POST = ("api", "-X", "POST", ISSUE, "--input", "-")


def fake(path, *bodies):
    """A GitHub whose target `path` holds a comment for each body, and that takes a post there."""
    gh = FakeGitHub()
    gh.answer_list(path, [{"body": body} for body in bodies])
    gh.answer(("api", "-X", "POST", path, "--input", "-"), "{}")
    return gh


def posted(gh):
    """The body of each comment posted, read back from the JSON on standard input."""
    return [json.loads(text)["body"] for args, text in gh.calls if args[:3] == ("api", "-X", "POST")]


class PostOnceTests(unittest.TestCase):
    def test_a_target_with_no_marker_comment_takes_one_post(self):
        gh = fake(ISSUE, "an earlier comment", "<!-- step:other -->\nanother key")
        self.assertTrue(comments.post_once(gh, "o/r", 5, MARKER, "The text."))
        self.assertEqual(posted(gh), [f"{MARKER}\nThe text."])
        self.assertEqual(gh.args(), [("api_list", ISSUE), POST])

    def test_a_marker_comment_on_the_target_skips_the_post(self):
        gh = fake(ISSUE, "first", f"{MARKER}\nThe text.")
        self.assertFalse(comments.post_once(gh, "o/r", 5, MARKER, "The text again."))
        self.assertEqual(posted(gh), [])

    def test_a_marker_on_a_later_page_skips_the_post(self):
        # api_list pages with --paginate, so has reads each item it gives: here the marker is in the 151st.
        gh = fake(ISSUE, *[f"comment {n}" for n in range(150)], f"{MARKER}\nlate")
        self.assertFalse(comments.post_once(gh, "o/r", 5, MARKER, "The text."))
        self.assertEqual(gh.args(), [("api_list", ISSUE)])

    def test_a_comment_with_no_body_holds_no_marker(self):
        gh = FakeGitHub()
        gh.answer_list(ISSUE, [{"body": None}, {}])
        gh.answer(POST, "{}")
        self.assertTrue(comments.post_once(gh, "o/r", 5, MARKER, "The text."))

    def test_a_number_is_the_target_before_a_sha(self):
        gh = fake(ISSUE)
        self.assertTrue(comments.post_once(gh, "o/r", "5", MARKER, "The text.", sha="abc"))
        self.assertEqual(gh.args(), [("api_list", ISSUE), POST])

    def test_no_number_posts_on_the_commit(self):
        gh = fake(COMMIT)
        self.assertTrue(comments.post_once(gh, "o/r", None, MARKER, "The text.", sha="abc"))
        self.assertEqual(posted(gh), [f"{MARKER}\nThe text."])
        self.assertEqual(gh.args(), [("api_list", COMMIT), ("api", "-X", "POST", COMMIT, "--input", "-")])

    def test_no_number_and_no_sha_is_an_error(self):
        with self.assertRaises(ValueError):
            comments.post_once(FakeGitHub(), "o/r", None, MARKER, "The text.")

    def test_a_failed_read_posts_nothing(self):
        gh = FakeGitHub()
        gh.answer_list(ISSUE, Fail(status=502))
        with self.assertRaises(GitHubError):
            comments.post_once(gh, "o/r", 5, MARKER, "The text.")
        self.assertEqual(gh.args(), [("api_list", ISSUE)])


class HasTests(unittest.TestCase):
    def test_has_reads_the_marker_anywhere_in_a_body(self):
        self.assertTrue(comments.has(fake(ISSUE, f"text\n{MARKER}"), "o/r", 5, MARKER))

    def test_has_reads_no_marker_of_another_key(self):
        self.assertFalse(comments.has(fake(ISSUE, "<!-- step:abd -->"), "o/r", 5, MARKER))

    def test_has_reads_the_commit_with_no_number(self):
        self.assertTrue(comments.has(fake(COMMIT, MARKER), "o/r", None, MARKER, sha="abc"))


if __name__ == "__main__":
    unittest.main()
