"""Unit tests of repo.py, for #555 and #571: each method sends its `gh` argument list and gives its parsed answer, and
each special case of `gh` or GitHub gives the answer its docstring states. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A FakeGitHub stands in for the adapter, so no
test calls GitHub."""

import json
import unittest

from fake_github import Fail, FakeGitHub, closes
from github import GitHubError
from repo import Repo

SHA = "a" * 40
FIELDS = (
    "number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url,state,labels,mergeCommit,mergedBy,"
    "changedFiles,closingIssuesReferences"
)
ISSUE_FIELDS = "number,title,body,state,labels,comments"
RUN_FIELDS = "databaseId,displayTitle,headSha,status,conclusion,url,createdAt"
ISSUE_COMMENTS = "repos/o/r/issues/5/comments"
COMMIT_COMMENTS = f"repos/o/r/commits/{SHA}/comments"


def repo():
    return Repo(FakeGitHub(), "o/r")


class ReadTests(unittest.TestCase):
    def test_pull_request_reads_the_fields_of_the_judge_and_the_repair_loop_with_gh_pr_view(self):
        r = repo()
        pr = {"number": 7, "headRefOid": SHA, **closes("o/r", 5)}
        r.gh.answer(("pr", "view", "7", "-R", "o/r", "--json", FIELDS), pr)
        self.assertEqual(r.pull_request(7), pr)

    def test_rest_pull_request_reads_the_rest_path(self):
        r = repo()
        r.gh.answer(("api", "repos/o/r/pulls/7"), {"head": {"sha": SHA}})
        self.assertEqual(r.rest_pull_request(7), {"head": {"sha": SHA}})

    def test_issue_reads_the_fields_of_the_judge_and_the_repair_loop_with_gh_issue_view(self):
        r = repo()
        issue = {"number": 5, "title": "The issue", "body": "The spec.", "state": "OPEN"}
        r.gh.answer(("issue", "view", "5", "-R", "o/r", "--json", ISSUE_FIELDS), issue)
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

    def test_comments_are_one_list_with_every_field_and_a_body_as_github_gives_it(self):
        # read.py read the comments of an issue as one text of bodies and as one JSON object for each line, with two
        # --paginate forms. It reads both from this one list now, with each body, null and empty ones too.
        r = repo()
        comments = [
            {"created_at": "2026-11-11T00:00:00Z", "user": {"login": "a"}, "body": "one\ntwo"},
            {"created_at": "2026-11-11T00:00:01Z", "user": None, "body": None},
            {"created_at": "2026-11-11T00:00:02Z", "user": {"login": "b"}, "body": ""},
        ]
        r.gh.answer_list(ISSUE_COMMENTS, comments)
        self.assertEqual(r.comments(5), comments)
        self.assertEqual(r.gh.args(), [("api_list", ISSUE_COMMENTS)])

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

    def test_comment_on_commit_sends_the_body_as_a_form_field(self):
        r = repo()
        call = ("api", COMMIT_COMMENTS, "-f", "body=The text.")
        r.gh.answer(call, "{}")
        r.comment_on_commit(SHA, "The text.")
        self.assertEqual(r.gh.args(), [call])


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


class ArgumentTests(unittest.TestCase):
    """Each method of the repair loop, from #571, sends the `gh` argument list that its caller sent before it, and gives
    the parsed answer."""

    def sent(self, call, output, method, *args):
        """The answer of the method, after a check that it sent `call` and nothing else."""
        r = repo()
        r.gh.answer(call, output)
        answer = getattr(r, method)(*args)
        self.assertEqual(r.gh.args(), [call])
        return answer, r

    def test_default_branch_reads_the_repo_with_jq(self):
        answer, _ = self.sent(("api", "repos/o/r", "--jq", ".default_branch"), "main\n", "default_branch")
        self.assertEqual(answer, "main")

    def test_file_at_reads_the_raw_content_at_the_commit(self):
        call = ("api", "-H", "Accept: application/vnd.github.raw+json", f"repos/o/r/contents/a/b.yml?ref={SHA}")
        answer, _ = self.sent(call, "on:\n", "file_at", "a/b.yml", SHA)
        self.assertEqual(answer, "on:\n")

    def test_has_branch_reads_the_ref(self):
        answer, _ = self.sent(("api", "repos/o/r/git/ref/heads/repair-base/7"), "{}", "has_branch", "repair-base/7")
        self.assertIs(answer, True)

    def test_create_branch_posts_the_ref_at_the_sha(self):
        call = ("api", "-X", "POST", "repos/o/r/git/refs", "-f", "ref=refs/heads/repair-base/7", "-f", f"sha={SHA}")
        self.sent(call, "", "create_branch", "repair-base/7", SHA)

    def test_move_branch_forces_the_ref_to_the_sha(self):
        call = ("api", "-X", "PATCH", "repos/o/r/git/refs/heads/repair-base/7", "-f", f"sha={SHA}", "-F", "force=true")
        self.sent(call, "", "move_branch", "repair-base/7", SHA)

    def test_delete_branch_deletes_the_ref(self):
        self.sent(
            ("api", "-X", "DELETE", "repos/o/r/git/refs/heads/repair-base/7"), "", "delete_branch", "repair-base/7"
        )

    def test_open_pull_request_lists_the_open_pull_requests_of_the_branch(self):
        call = ("pr", "list", "-R", "o/r", "--head", "fix/x", "--state", "open", "--json", "number,headRefOid,labels")
        pr = {"number": 8, "headRefOid": SHA, "labels": []}
        answer, _ = self.sent(call, [pr, {"number": 9}], "open_pull_request", "fix/x")
        self.assertEqual(answer, pr)
        answer, _ = self.sent(call, [], "open_pull_request", "fix/x")
        self.assertIsNone(answer)

    def test_label_pull_request_edits_the_pull_request(self):
        self.sent(("pr", "edit", "5", "-R", "o/r", "--add-label", "repair"), "", "label_pull_request", 5, "repair")

    def test_enable_auto_merge_arms_a_squash_at_the_head(self):
        call = ("pr", "merge", "7", "-R", "o/r", "--auto", "--squash", "--delete-branch", "--match-head-commit", SHA)
        self.sent(call, "", "enable_auto_merge", 7, SHA)

    def test_disable_auto_merge_disarms(self):
        self.sent(("pr", "merge", "7", "-R", "o/r", "--disable-auto"), "", "disable_auto_merge", 7)

    def test_issues_with_label_lists_every_state_up_to_200(self):
        call = (
            "issue",
            "list",
            "-R",
            "o/r",
            "--label",
            "repair",
            "--state",
            "all",
            "--limit",
            "200",
            "--json",
            "number,body,state,labels",
        )
        answer, _ = self.sent(call, [{"number": 4}], "issues_with_label", "repair")
        self.assertEqual(answer, [{"number": 4}])

    def test_open_issues_with_labels_lists_the_open_issues_with_each_label(self):
        call = (
            "issue",
            "list",
            "-R",
            "o/r",
            "--label",
            "repair",
            "--label",
            "factory",
            "--state",
            "open",
            "--json",
            "number",
        )
        answer, _ = self.sent(call, [{"number": 4}], "open_issues_with_labels", ("repair", "factory"))
        self.assertEqual(answer, [{"number": 4}])

    def test_create_issue_sends_each_label_and_the_body_on_standard_input(self):
        call = (
            "issue",
            "create",
            "-R",
            "o/r",
            "--title",
            "T",
            "--label",
            "repair",
            "--label",
            "ready",
            "--body-file",
            "-",
        )
        answer, r = self.sent(
            call, "https://github.com/o/r/issues/7\n", "create_issue", "T", "The body.", ("repair", "ready")
        )
        self.assertEqual(answer, "https://github.com/o/r/issues/7")
        self.assertEqual(r.gh.input_of(*call), "The body.")

    def test_comment_on_issue_uses_gh_issue_comment(self):
        self.sent(("issue", "comment", "4", "-R", "o/r", "--body", "The text."), "", "comment_on_issue", 4, "The text.")

    def test_close_issue_closes_with_the_comment(self):
        self.sent(("issue", "close", "5", "-R", "o/r", "--comment", "The text."), "", "close_issue", 5, "The text.")

    def test_workflow_run_reads_the_rest_path(self):
        answer, _ = self.sent(("api", "repos/o/r/actions/runs/9"), {"head_branch": "main"}, "workflow_run", 9)
        self.assertEqual(answer, {"head_branch": "main"})

    def test_workflow_runs_lists_30_runs_with_each_filter_as_a_flag(self):
        flags = ("--branch", "main", "--status", "success", "--event", "workflow_dispatch")
        call = ("run", "list", "-R", "o/r", "--workflow", "factory.yml", *flags, "--limit", "30", "--json", RUN_FIELDS)
        answer, _ = self.sent(
            call, [{"databaseId": 2}], "workflow_runs", "factory.yml", "main", "success", "workflow_dispatch"
        )
        self.assertEqual(answer, [{"databaseId": 2}])

    def test_workflow_runs_with_no_filter_sends_no_flag(self):
        call = ("run", "list", "-R", "o/r", "--workflow", "factory.yml", "--limit", "30", "--json", RUN_FIELDS)
        self.sent(call, [], "workflow_runs", "factory.yml")

    def test_run_status_reads_the_status_and_the_conclusion(self):
        call = ("run", "view", "9", "-R", "o/r", "--json", "status,conclusion")
        answer, _ = self.sent(call, {"status": "completed", "conclusion": "success"}, "run_status", 9)
        self.assertEqual(answer, {"status": "completed", "conclusion": "success"})

    def test_failed_log_reads_the_log_of_the_attempt(self):
        call = ("run", "view", "9", "-R", "o/r", "--attempt", "2", "--log-failed")
        answer, _ = self.sent(call, "the log\n", "failed_log", 9, 2)
        self.assertEqual(answer, ("the log\n", None))

    def test_rerun_failed_reruns_the_failed_jobs(self):
        self.sent(("run", "rerun", "9", "-R", "o/r", "--failed"), "", "rerun_failed", 9)

    def test_dispatch_workflow_sends_the_ref_and_each_input_in_order(self):
        call = ("workflow", "run", "ci.yml", "-R", "o/r", "--ref", "main", "-f", "a=1", "-f", "b=x")
        self.sent(call, "", "dispatch_workflow", "ci.yml", "main", {"a": 1, "b": "x"})

    def test_dispatch_workflow_with_no_ref_is_at_the_default_branch(self):
        self.sent(("workflow", "run", "ci.yml", "-R", "o/r"), "", "dispatch_workflow", "ci.yml")


class SpecialCaseTests(unittest.TestCase):
    """Each fact of the table of #571's change 3, and the 404 of a ref, as Repo answers it."""

    RAW = ("api", "-H", "Accept: application/vnd.github.raw+json", f"repos/o/r/contents/f.yml?ref={SHA}")
    LOG = ("run", "view", "9", "-R", "o/r", "--attempt", "1", "--log-failed")
    REF = ("api", "repos/o/r/git/ref/heads/b")
    MATCHING = ("api", "repos/o/r/git/matching-refs/heads/repair/5")
    DELETE = ("api", "--method", "DELETE", "repos/o/r/git/refs/heads/repair/5")

    def test_a_404_on_the_content_of_a_file_is_none(self):
        r = repo()
        r.gh.answer(self.RAW, Fail(status=404))
        self.assertIsNone(r.file_at("f.yml", SHA))

    def test_any_other_failure_of_the_content_raises(self):
        for status in (None, 502):
            with self.subTest(status=status):
                r = repo()
                r.gh.answer(self.RAW, Fail(status=status))
                with self.assertRaises(GitHubError):
                    r.file_at("f.yml", SHA)

    def test_a_failed_log_that_exits_1_with_output_gives_the_output(self):
        r = repo()
        r.gh.answer(self.LOG, Fail(stdout="the other jobs\n", stderr="log not found"))
        self.assertEqual(r.failed_log(9, 1), ("the other jobs\n", "log not found"))

    def test_a_failed_log_that_exits_1_with_no_output_raises(self):
        r = repo()
        r.gh.answer(self.LOG, Fail(stdout=" \n", stderr="log not found"))
        with self.assertRaises(GitHubError) as raised:
            r.failed_log(9, 1)
        self.assertIn("log not found", raised.exception.stderr)

    def test_a_failed_log_with_another_exit_code_raises_with_output_or_none(self):
        for stdout in ("partial\n", ""):
            with self.subTest(stdout=stdout):
                r = repo()
                r.gh.answer(self.LOG, Fail(stdout=stdout, stderr="killed", returncode=2))
                with self.assertRaises(GitHubError):
                    r.failed_log(9, 1)

    def test_a_branch_that_exists_is_deleted(self):
        r = repo()
        r.gh.answer(self.MATCHING, [{"ref": "refs/heads/repair/50"}, {"ref": "refs/heads/repair/5"}])
        r.gh.answer(self.DELETE, "")
        self.assertIs(r.delete_branch_if_present("repair/5"), True)
        self.assertEqual(r.gh.args(), [self.MATCHING, self.DELETE])

    def test_a_branch_that_is_gone_or_only_a_prefix_of_another_is_not_deleted(self):
        for refs in ([], [{"ref": "refs/heads/repair/50"}]):
            with self.subTest(refs=refs):
                r = repo()
                r.gh.answer(self.MATCHING, refs)
                self.assertIs(r.delete_branch_if_present("repair/5"), False)
                self.assertEqual(r.gh.args(), [self.MATCHING])

    def test_an_issue_create_that_prints_no_url_gives_the_empty_text(self):
        for output in ("", " \n"):
            with self.subTest(output=output):
                r = repo()
                r.gh.answer(("issue", "create", "-R", "o/r", "--title", "T", "--body-file", "-"), output)
                self.assertEqual(r.create_issue("T", "The body.", ()), "")

    def test_a_404_on_a_ref_is_no_branch(self):
        r = repo()
        r.gh.answer(self.REF, Fail(status=404))
        self.assertIs(r.has_branch("b"), False)

    def test_any_other_failure_of_a_ref_raises(self):
        for status in (None, 403, 502):
            with self.subTest(status=status):
                r = repo()
                r.gh.answer(self.REF, Fail(status=status, stderr="gh: the reason"))
                with self.assertRaises(GitHubError):
                    r.has_branch("b")


if __name__ == "__main__":
    unittest.main()
