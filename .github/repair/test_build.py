"""Unit tests of build.py: the adapter of the create, the move and the deletion of repair-base/<n>, decided in #403,
the run that the dispatch gives back, from #469, the read of the traveler of the issue, from #513, and the author
filter of the `Opened <url>` comment. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A FakeGitHub of .github/shared/ stands in for each
adapter, so no test calls GitHub."""

import os
import unittest
from unittest import mock

import build
import traveler
from fake_github import Fail, FakeGitHub

REPO = "owner/repo"
BRANCH = "repair-base/7"
SHA = "0" * 40
REF = ("api", f"repos/{REPO}/git/ref/heads/{BRANCH}")
POST = ("api", "-X", "POST", f"repos/{REPO}/git/refs", "-f", f"ref=refs/heads/{BRANCH}", "-f", f"sha={SHA}")
PATCH = ("api", "-X", "PATCH", f"repos/{REPO}/git/refs/heads/{BRANCH}", "-f", f"sha={SHA}", "-F", "force=true")
DELETE = ("api", "-X", "DELETE", f"repos/{REPO}/git/refs/heads/{BRANCH}")
RUN_LIST = (
    "run",
    "list",
    "-R",
    REPO,
    "--workflow",
    build.WORKFLOW,
    "--event",
    "workflow_dispatch",
    "--limit",
    "30",
    "--json",
    "databaseId,displayTitle,status,conclusion,url",
)
ISSUE_VIEW = ("issue", "view", "7", "-R", REPO, "--json", "number,state,body,labels,comments")
OPENED = f"Opened https://github.com/{REPO}/pull/5"
FACTORY_PR = {"state": "OPEN", "headRefName": "factory/1", "body": "The fix.\n\nCloses #7\n"}


def issue(labels=("repair",), comments=(), **fields):
    ticket = traveler.Traveler(
        run="https://github.com/o/r/actions/runs/9", target="`fix/x`", sha=SHA, session="sesn_1", **fields
    )
    return {
        "number": 7,
        "state": "OPEN",
        "labels": [{"name": name} for name in labels],
        "comments": [{"author": {"login": login}, "body": body} for login, body in comments],
        "body": traveler.render(ticket),
    }


class AdapterTests(unittest.TestCase):
    def test_the_ref_adapter_holds_the_ref_token(self):
        with mock.patch.dict(os.environ, {"GH_TOKEN": "job", "REPAIR_REF_TOKEN": "app"}):
            job, app = build.adapters()
        self.assertEqual((job.token, app.token), ("job", "app"))

    def test_with_no_ref_token_the_ref_adapter_is_the_job_adapter(self):
        for value in (None, ""):
            with self.subTest(ref_token=value), mock.patch.dict(os.environ, {"GH_TOKEN": "job"}):
                if value is None:
                    os.environ.pop("REPAIR_REF_TOKEN", None)
                else:
                    os.environ["REPAIR_REF_TOKEN"] = value
                job, app = build.adapters()
                self.assertIs(app, job)
                self.assertEqual(job.token, "job")


class BaseBranchTests(unittest.TestCase):
    def test_a_missing_branch_gets_a_post(self):
        app = FakeGitHub()
        app.answer(REF, Fail(status=404))
        app.answer(POST, "")
        build.create_base_branch(app, REPO, BRANCH, SHA)
        self.assertEqual(app.args(), [REF, POST])

    def test_a_branch_left_by_an_earlier_run_gets_a_patch(self):
        app = FakeGitHub()
        app.answer(REF, "{}")
        app.answer(PATCH, "")
        build.create_base_branch(app, REPO, BRANCH, SHA)
        self.assertEqual(app.args(), [REF, PATCH])

    def test_any_other_failure_of_the_read_exits_with_no_write(self):
        for status in (None, 403, 502):
            with self.subTest(status=status):
                app = FakeGitHub()
                app.answer(REF, Fail(status=status, stderr="gh: the reason"))
                with self.assertRaises(SystemExit) as stop:
                    build.create_base_branch(app, REPO, BRANCH, SHA)
                self.assertIn("the reason", stop.exception.code)
                self.assertEqual(app.args(), [REF])

    def test_a_failed_write_exits_with_its_text(self):
        app = FakeGitHub()
        app.answer(REF, Fail(status=404))
        app.answer(POST, Fail(status=422, stderr="gh: Reference update failed (HTTP 422)"))
        with self.assertRaises(SystemExit) as stop:
            build.create_base_branch(app, REPO, BRANCH, SHA)
        self.assertIn("could not be created at", stop.exception.code)

    def test_a_failed_deletion_is_a_log_line(self):
        job = FakeGitHub()
        job.answer(DELETE, Fail(status=422))
        build.delete_base_branch(job, REPO, BRANCH)
        self.assertEqual(job.args(), [DELETE])

    def test_a_factory_repair_creates_the_branch_on_the_ref_adapter_and_deletes_it_on_the_job_adapter(self):
        job, app = FakeGitHub("job"), FakeGitHub("app")
        job.answer(ISSUE_VIEW, issue(labels=("repair", "factory")), issue(comments=[("owner", OPENED)]))
        job.answer(
            (
                "run",
                "list",
                "-R",
                REPO,
                "--workflow",
                build.WORKFLOW,
                "--status",
                "success",
                "--branch",
                "main",
                "--limit",
                "1",
                "--json",
                "headSha",
            ),
            [{"headSha": SHA}],
        )
        own = {"databaseId": 3, "displayTitle": "#7", "url": "https://example/3"}
        job.answer(RUN_LIST, [], [own])
        dispatch = ("workflow", "run", build.WORKFLOW, "-R", REPO, "--ref", BRANCH, "-f", "base=main", "-f", "issue=7")
        job.answer((*dispatch, "-f", "copilot=on"), "")
        job.answer(("pr", "view", "5", "-R", REPO, "--json", "state,headRefName,body"), FACTORY_PR)
        job.answer(("pr", "edit", "5", "-R", REPO, "--add-label", build.LABEL), "")
        job.answer(DELETE, "")
        app.answer(REF, Fail(status=404))
        app.answer(POST, "")
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            mock.patch.object(build.time, "sleep"),
        ):
            build.main(job, app)
        self.assertEqual(app.args(), [REF, POST])
        self.assertEqual([args for args in job.args() if "git/ref" in " ".join(args)], [DELETE])
        self.assertEqual(job.args()[-1], DELETE)
        self.assertEqual(job.count("pr", "edit", "5", "-R", REPO, "--add-label", build.LABEL), 1)

    def test_the_branch_is_deleted_when_the_create_fails(self):
        job, app = FakeGitHub("job"), FakeGitHub("app")
        job.answer(ISSUE_VIEW, issue(labels=("repair", "factory")))
        green = ("run", "list", "-R", REPO, "--workflow", build.WORKFLOW, "--status", "success", "--branch", "main")
        job.answer((*green, "--limit", "1", "--json", "headSha"), [{"headSha": SHA}])
        job.answer(DELETE, "")
        app.answer(REF, Fail(status=502))
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            self.assertRaises(SystemExit),
        ):
            build.main(job, app)
        self.assertEqual(app.args(), [REF])
        self.assertEqual(job.args()[-1], DELETE)


class DispatchTests(unittest.TestCase):
    def test_the_dispatch_gives_back_the_run_of_its_issue_when_another_issue_comes_first(self):
        other = {"databaseId": 2, "displayTitle": "#999", "url": "https://example/2"}
        own = {"databaseId": 3, "displayTitle": "#7", "url": "https://example/3"}
        old = {"databaseId": 1, "displayTitle": "#7", "url": "https://example/1"}
        gh = FakeGitHub()
        gh.answer(RUN_LIST, [old], [other, own, old])
        gh.answer(("workflow", "run", build.WORKFLOW, "-R", REPO, "-f", "issue=7", "-f", "copilot=on"), "")
        with mock.patch.object(build.time, "sleep"):
            run = build.dispatch(gh, REPO, 7, 180)
        self.assertEqual(run, own)


class OpenedTests(unittest.TestCase):
    """The pull request of `Opened <url>`, posted by the owner of the repo alone, since the factory posts under
    FACTORY_SELF_TOKEN."""

    def opened(self, comments, pr=FACTORY_PR):
        gh = FakeGitHub()
        gh.answer(("pr", "view", "5", "-R", REPO, "--json", "state,headRefName,body"), pr)
        return build.opened_pull_request(gh, REPO, issue(comments=comments)), gh

    def test_the_owner_s_comment_names_the_pull_request(self):
        self.assertEqual(self.opened([("owner", OPENED)])[0], 5)

    def test_a_comment_by_anyone_else_is_no_result_and_reads_no_pull_request(self):
        for login in ("someone", "owner-x", "github-actions[bot]"):
            with self.subTest(login=login):
                pr, gh = self.opened([(login, OPENED)])
                self.assertIsNone(pr)
                self.assertEqual(gh.calls, [])

    def test_a_comment_in_another_form_is_no_result(self):
        for body in (f"See {OPENED}", "Opened https://github.com/other/repo/pull/5", f"{OPENED}/files"):
            with self.subTest(body=body):
                self.assertIsNone(self.opened([("owner", body)])[0])

    def test_a_pull_request_that_is_not_the_factory_s_is_no_result(self):
        for pr in ({**FACTORY_PR, "state": "CLOSED"}, {**FACTORY_PR, "headRefName": "fix/x"}):
            with self.subTest(pr=pr):
                self.assertIsNone(self.opened([("owner", OPENED)], pr)[0])


TARGET_COMMENT = (
    "Every repair lands on the head of `main`, and the fault is on the branch of pull request #3. "
    "No build. A builder at the red SHA is the Managed Agents builder, #370."
)


class TravelerTests(unittest.TestCase):
    def run_main(self, issue):
        gh = FakeGitHub()
        gh.answer(ISSUE_VIEW, issue)
        gh.answer(("issue", "comment", "7", "-R", REPO, "--body", TARGET_COMMENT), "")
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            mock.patch.object(build, "build") as built,
        ):
            build.main(gh)
        return gh, built

    def test_an_issue_whose_traveler_targets_a_pull_request_gets_one_comment_and_no_build(self):
        gh, built = self.run_main(issue(pull_request=3))
        built.assert_not_called()
        self.assertEqual(gh.count("issue", "comment", "7", "-R", REPO, "--body", TARGET_COMMENT), 1)

    def test_an_issue_whose_traveler_targets_main_is_built(self):
        gh, built = self.run_main(issue())
        self.assertEqual(gh.args(), [ISSUE_VIEW])
        built.assert_called_once_with(gh, REPO, 7, 3600)

    def test_a_factory_pull_request_closes_its_issue_alone_on_a_line(self):
        for body, expected in (("The fix.\n\nCloses #7\n", True), ("Closes #8\n", False), ("See Closes #7.", False)):
            gh = FakeGitHub()
            gh.answer(("pr", "view", "5", "-R", REPO, "--json", "state,headRefName,body"), {**FACTORY_PR, "body": body})
            with self.subTest(body=body):
                self.assertIs(build.is_factory_pull_request(gh, REPO, 5, 7), expected)


if __name__ == "__main__":
    unittest.main()
