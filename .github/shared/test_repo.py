"""Unit tests of repo.py, for #555: each method sends its `gh` argument list and gives its parsed answer. The build job
of ci.yml runs them with `python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A FakeGitHub stands in for
the adapter, so no test calls GitHub."""

import json
import unittest

from fake_github import Fail, FakeGitHub, closes
from github import GitHubError
from repo import Repo

SHA = "a" * 40
FIELDS = "number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url,closingIssuesReferences"
ISSUE_COMMENTS = "repos/o/r/issues/5/comments"
COMMIT_COMMENTS = f"repos/o/r/commits/{SHA}/comments"


def repo():
    return Repo(FakeGitHub(), "o/r")


class ReadTests(unittest.TestCase):
    def test_pull_request_reads_the_nine_fields_of_the_judge_with_gh_pr_view(self):
        r = repo()
        pr = {"number": 7, "headRefOid": SHA, **closes("o/r", 5)}
        r.gh.answer(("pr", "view", "7", "-R", "o/r", "--json", FIELDS), pr)
        self.assertEqual(r.pull_request(7), pr)

    def test_rest_pull_request_reads_the_rest_path(self):
        r = repo()
        r.gh.answer(("api", "repos/o/r/pulls/7"), {"head": {"sha": SHA}})
        self.assertEqual(r.rest_pull_request(7), {"head": {"sha": SHA}})

    def test_issue_reads_its_number_title_and_body(self):
        r = repo()
        issue = {"number": 5, "title": "The issue", "body": "The spec."}
        r.gh.answer(("issue", "view", "5", "-R", "o/r", "--json", "number,title,body"), issue)
        self.assertEqual(r.issue(5), issue)

    def test_commits_and_files_read_every_page_of_the_pull_request(self):
        r = repo()
        r.gh.answer_list("repos/o/r/pulls/7/commits", [{"sha": SHA}])
        r.gh.answer_list("repos/o/r/pulls/7/files", [{"filename": "a.py"}])
        self.assertEqual(r.commits(7), [{"sha": SHA}])
        self.assertEqual(r.files(7), [{"filename": "a.py"}])

    def test_commit_reads_the_rest_path_of_the_sha(self):
        r = repo()
        r.gh.answer(("api", f"repos/o/r/commits/{SHA}"), {"stats": {"total": 2}})
        self.assertEqual(r.commit(SHA), {"stats": {"total": 2}})

    def test_a_failed_call_raises_github_error(self):
        r = repo()
        r.gh.answer(("api", "repos/o/r/pulls/7"), Fail(status=404))
        with self.assertRaises(GitHubError) as raised:
            r.rest_pull_request(7)
        self.assertEqual(raised.exception.status, 404)


class CommentTests(unittest.TestCase):
    def test_comments_of_a_number_read_every_page_of_the_issue(self):
        r = repo()
        r.gh.answer_list(ISSUE_COMMENTS, [{"body": "a"}])
        self.assertEqual(r.comments(5), [{"body": "a"}])

    def test_a_number_is_the_target_before_a_sha(self):
        r = repo()
        r.gh.answer_list(ISSUE_COMMENTS, [])
        r.gh.answer(("api", "-X", "POST", ISSUE_COMMENTS, "--input", "-"), "{}")
        r.comments("5", SHA)
        r.post_comment("5", "The text.", SHA)
        self.assertEqual(
            r.gh.args(), [("api_list", ISSUE_COMMENTS), ("api", "-X", "POST", ISSUE_COMMENTS, "--input", "-")]
        )

    def test_no_number_reads_and_posts_on_the_commit(self):
        r = repo()
        r.gh.answer_list(COMMIT_COMMENTS, [{"body": "a"}])
        post = ("api", "-X", "POST", COMMIT_COMMENTS, "--input", "-")
        r.gh.answer(post, "{}")
        self.assertEqual(r.comments(None, SHA), [{"body": "a"}])
        r.post_comment(None, "The text.", SHA)
        self.assertEqual(json.loads(r.gh.input_of(*post)), {"body": "The text."})

    def test_post_comment_sends_the_body_as_json_on_standard_input(self):
        r = repo()
        post = ("api", "-X", "POST", ISSUE_COMMENTS, "--input", "-")
        r.gh.answer(post, "{}")
        r.post_comment(5, '<!-- m -->\nA "quoted" text.')
        self.assertEqual(json.loads(r.gh.input_of(*post)), {"body": '<!-- m -->\nA "quoted" text.'})

    def test_neither_a_number_nor_a_sha_is_an_error(self):
        r = repo()
        with self.assertRaises(ValueError):
            r.comments(None)
        with self.assertRaises(ValueError):
            r.post_comment(None, "The text.")
        self.assertEqual(r.gh.calls, [])


class CheckTests(unittest.TestCase):
    LATEST = ("api", f"repos/o/r/commits/{SHA}/check-runs?check_name=a-check&filter=latest")

    def test_post_check_posts_a_completed_check_run_on_the_commit(self):
        r = repo()
        post = (
            "api",
            "repos/o/r/check-runs",
            "-f",
            "name=a-check",
            "-f",
            f"head_sha={SHA}",
            "-f",
            "status=completed",
            "-f",
            "conclusion=failure",
            "-f",
            "output[title]=The title",
            "-f",
            "output[summary]=The summary",
        )
        r.gh.answer(post, "{}")
        r.post_check("a-check", SHA, "failure", "The title", "The summary")
        self.assertEqual(r.gh.args(), [post])

    def test_latest_check_gives_the_status_and_the_conclusion(self):
        r = repo()
        r.gh.answer(self.LATEST, {"check_runs": [{"status": "completed", "conclusion": "success"}]})
        self.assertEqual(r.latest_check("a-check", SHA), ("completed", "success"))

    def test_a_check_run_in_progress_has_no_conclusion(self):
        r = repo()
        r.gh.answer(self.LATEST, {"check_runs": [{"status": "in_progress"}]})
        self.assertEqual(r.latest_check("a-check", SHA), ("in_progress", None))

    def test_an_empty_list_is_none(self):
        r = repo()
        r.gh.answer(self.LATEST, {"check_runs": []})
        self.assertIsNone(r.latest_check("a-check", SHA))


if __name__ == "__main__":
    unittest.main()
