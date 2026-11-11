"""Unit tests of build.py: the token of the create, the move and the deletion of repair-base/<n>, decided in #403,
the run that the dispatch gives back, from #469, the read of the traveler of the issue, from #513, and the author
filter of the `Opened <url>` comment. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. A FakeRepo of .github/shared/ stands in for each
Repo, so no test calls GitHub. A 404 on the read of a ref is a fact of repo.py, and test_repo.py tests it."""

import os
import unittest
from unittest import mock

import build
import runs
import traveler
from fake_github import closes
from fake_repo import Fail, FakeRepo

REPO = "owner/repo"
BRANCH = "repair-base/7"
SHA = "0" * 40
# A `createdAt` far in the future, so it is after the `since` of each dispatch.
NOW = "2100-01-01T00:00:00Z"
# The calls of the branch, each as (method, arguments), as FakeRepo.calls holds them.
REF = ("has_branch", {"branch": BRANCH})
POST = ("create_branch", {"branch": BRANCH, "sha": SHA})
PATCH = ("move_branch", {"branch": BRANCH, "sha": SHA})
DELETE = ("delete_branch", {"branch": BRANCH})
BRANCH_CALLS = {"has_branch", "create_branch", "move_branch", "delete_branch"}
GREEN = (build.WORKFLOW, "main", "success", None)
LABEL = ("label_pull_request", {"number": 5, "label": build.LABEL})
OPENED = f"Opened https://github.com/{REPO}/pull/5"
FACTORY_PR = {"state": "OPEN", "headRefName": "factory/1", **closes(REPO, 7)}


def fake(name=REPO):
    """A repo whose branch calls each succeed, so a test answers only the reads it scripts."""
    repo = FakeRepo(name)
    for method in ("create_branch", "move_branch", "delete_branch"):
        repo.answer(method, (BRANCH, SHA)[: 1 if method == "delete_branch" else 2], None)
    repo.answer("label_pull_request", (5, build.LABEL), None)
    return repo


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


def calls(repo, names=None):
    """Each call of the repo as (method, arguments), or only the calls of the methods in `names`."""
    return [call for call in repo.calls if names is None or call[0] in names]


class TokenTests(unittest.TestCase):
    def test_the_ref_repo_holds_the_ref_token(self):
        with mock.patch.dict(os.environ, {"GH_TOKEN": "job", "REPAIR_REF_TOKEN": "app", "GITHUB_REPOSITORY": REPO}):
            job, app = build.repos()
        self.assertEqual((job.gh.token, app.gh.token), ("job", "app"))
        self.assertEqual((job.name, app.name), (REPO, REPO))

    def test_with_no_ref_token_the_ref_repo_is_the_job_repo(self):
        for value in (None, ""):
            env = {"GH_TOKEN": "job", "GITHUB_REPOSITORY": REPO}
            with self.subTest(ref_token=value), mock.patch.dict(os.environ, env):
                if value is None:
                    os.environ.pop("REPAIR_REF_TOKEN", None)
                else:
                    os.environ["REPAIR_REF_TOKEN"] = value
                job, app = build.repos()
                self.assertIs(app, job)
                self.assertEqual(job.gh.token, "job")


class BaseBranchTests(unittest.TestCase):
    def test_a_missing_branch_is_created(self):
        app = fake()
        app.answer("has_branch", (BRANCH,), False)
        build.create_base_branch(app, BRANCH, SHA)
        self.assertEqual(calls(app), [REF, POST])

    def test_a_branch_left_by_an_earlier_run_is_moved(self):
        app = fake()
        app.answer("has_branch", (BRANCH,), True)
        build.create_base_branch(app, BRANCH, SHA)
        self.assertEqual(calls(app), [REF, PATCH])

    def test_a_failed_read_exits_with_no_write(self):
        for status in (None, 403, 502):
            with self.subTest(status=status):
                app = fake()
                app.answer("has_branch", (BRANCH,), Fail(status=status, stderr="gh: the reason"))
                with self.assertRaises(SystemExit) as stop:
                    build.create_base_branch(app, BRANCH, SHA)
                self.assertIn("could not be read", stop.exception.code)
                self.assertIn("the reason", stop.exception.code)
                self.assertEqual(calls(app), [REF])

    def test_a_failed_write_exits_with_its_text(self):
        for exists, verb, call in ((False, "created at", POST), (True, "moved to", PATCH)):
            with self.subTest(exists=exists):
                app = fake()
                app.answer("has_branch", (BRANCH,), exists)
                app.answer(call[0], tuple(call[1].values()), Fail(status=422, stderr="gh: Reference update failed"))
                with self.assertRaises(SystemExit) as stop:
                    build.create_base_branch(app, BRANCH, SHA)
                self.assertIn(f"could not be {verb} {SHA}", stop.exception.code)
                self.assertIn("Reference update failed", stop.exception.code)

    def test_a_failed_deletion_is_a_log_line(self):
        job = FakeRepo(REPO)
        job.answer("delete_branch", (BRANCH,), Fail(status=422))
        build.delete_base_branch(job, BRANCH)
        self.assertEqual(calls(job), [DELETE])

    def test_a_factory_repair_creates_the_branch_on_the_ref_repo_and_deletes_it_on_the_job_repo(self):
        job, app = fake(), fake()
        job.answer("issue", (7,), issue(labels=("repair", "factory")), issue(comments=[("owner", OPENED)]))
        job.answer("workflow_runs", GREEN, [{"headSha": SHA}])
        own = {"databaseId": 3, "displayTitle": "#7", "url": "https://example/3", "createdAt": NOW}
        job.answer("workflow_runs", (build.WORKFLOW, BRANCH, None, "workflow_dispatch"), [], [own])
        inputs = {"issue": 7, "base": "main", "copilot": "on"}
        job.answer("dispatch_workflow", (build.WORKFLOW, BRANCH, inputs), None)
        job.answer("pull_request", (5,), FACTORY_PR)
        app.answer("has_branch", (BRANCH,), False)
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            mock.patch.object(runs.time, "sleep"),
        ):
            build.main(job, app)
        self.assertEqual(calls(app), [REF, POST])
        self.assertEqual(calls(job, BRANCH_CALLS), [DELETE])
        self.assertEqual(job.calls[-1], DELETE)
        [dispatch] = job.calls_of("dispatch_workflow")
        self.assertEqual(list(dispatch["inputs"].items()), list(inputs.items()))
        self.assertEqual(calls(job, {"label_pull_request"}), [LABEL])

    def test_with_one_repo_every_ref_call_runs_on_it(self):
        repo = fake()
        repo.answer("issue", (7,), issue(labels=("repair", "factory")))
        repo.answer("workflow_runs", GREEN, [{"headSha": SHA}])
        repo.answer("has_branch", (BRANCH,), Fail(status=502))
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            self.assertRaises(SystemExit),
        ):
            build.main(repo)
        self.assertEqual(repo.calls[-2:], [REF, DELETE])

    def test_with_no_green_run_one_comment_and_no_build(self):
        repo = FakeRepo(REPO)
        repo.answer("issue", (7,), issue(labels=("repair", "factory")))
        repo.answer("workflow_runs", GREEN, [])
        text = (
            f"No run of `{build.WORKFLOW}` on `main` ended `success`, so the factory has no green ref to build this "
            "repair at. No build."
        )
        repo.answer("post_comment", (7, text), None)
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
        ):
            build.main(repo)
        green = dict(zip(("workflow", "branch", "status", "event"), GREEN))
        self.assertEqual(
            calls(repo),
            [
                ("issue", {"number": 7}),
                ("workflow_runs", green),
                ("post_comment", {"number": 7, "body": text, "sha": None}),
            ],
        )

    def test_the_branch_is_deleted_when_the_create_fails(self):
        job, app = fake(), fake()
        job.answer("issue", (7,), issue(labels=("repair", "factory")))
        job.answer("workflow_runs", GREEN, [{"headSha": SHA}])
        app.answer("has_branch", (BRANCH,), Fail(status=502))
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            self.assertRaises(SystemExit),
        ):
            build.main(job, app)
        self.assertEqual(calls(app), [REF])
        self.assertEqual(job.calls[-1], DELETE)


class DispatchTests(unittest.TestCase):
    """build() at main dispatches through runs.dispatch with `copilot=on` alone, and waits for the pull request while
    the run is in progress."""

    def build(self, issues, states):
        repo = fake()
        own = {"databaseId": 3, "displayTitle": "#7", "url": "https://example/3", "createdAt": NOW}
        repo.answer("workflow_runs", (build.WORKFLOW, None, None, "workflow_dispatch"), [], [own])
        repo.answer("dispatch_workflow", (build.WORKFLOW, None, {"issue": 7, "copilot": "on"}), None)
        repo.answer("issue", (7,), *issues)
        repo.answer(
            "run_status", (3,), *[{"status": status, "conclusion": conclusion} for status, conclusion in states]
        )
        repo.answer("pull_request", (5,), FACTORY_PR)
        with (
            mock.patch.dict(os.environ, {"GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(runs.time, "sleep") as sleep,
        ):
            build.build(repo, 7, 3600)
        return repo, sleep

    def test_the_pull_request_opened_while_the_run_is_in_progress_is_labeled(self):
        repo, sleep = self.build([issue(), issue(), issue(comments=[("owner", OPENED)])], [("in_progress", "")])
        self.assertEqual(calls(repo, {"label_pull_request"}), [LABEL])
        self.assertEqual(len(repo.calls_of("run_status")), 2)
        self.assertEqual([call.args for call in sleep.call_args_list], [(build.POLL,), (build.POLL,)])

    def test_a_run_that_ends_with_no_pull_request_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.build([issue()], [("completed", "failure")])
        self.assertIn("ended failure with no pull request on issue #7", stop.exception.code)

    def test_the_pull_request_posted_as_the_run_ends_is_labeled(self):
        repo, _ = self.build([issue(), issue(comments=[("owner", OPENED)])], [("completed", "success")])
        self.assertEqual(calls(repo, {"label_pull_request"}), [LABEL])


class OpenedTests(unittest.TestCase):
    """The pull request of `Opened <url>`, posted by the owner of the repo alone, since the factory posts under
    FACTORY_SELF_TOKEN."""

    def opened(self, comments, pr=FACTORY_PR):
        repo = FakeRepo(REPO)
        repo.answer("pull_request", (5,), pr)
        return build.opened_pull_request(repo, issue(comments=comments)), repo

    def test_the_owner_s_comment_names_the_pull_request(self):
        self.assertEqual(self.opened([("owner", OPENED)])[0], 5)

    def test_a_comment_by_anyone_else_is_no_result_and_reads_no_pull_request(self):
        for login in ("someone", "owner-x", "github-actions[bot]"):
            with self.subTest(login=login):
                pr, repo = self.opened([(login, OPENED)])
                self.assertIsNone(pr)
                self.assertEqual(repo.calls, [])

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
        repo = FakeRepo(REPO)
        repo.answer("issue", (7,), issue)
        repo.answer("post_comment", (7, TARGET_COMMENT), None)
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GITHUB_OUTPUT": os.devnull}),
            mock.patch.object(build.sys, "argv", ["build.py", "7"]),
            mock.patch.object(build, "build") as built,
        ):
            build.main(repo)
        return repo, built

    def test_an_issue_whose_traveler_targets_a_pull_request_gets_one_comment_and_no_build(self):
        repo, built = self.run_main(issue(pull_request=3))
        built.assert_not_called()
        self.assertEqual(repo.calls_of("post_comment"), [{"number": 7, "body": TARGET_COMMENT, "sha": None}])

    def test_an_issue_whose_traveler_targets_main_is_built(self):
        repo, built = self.run_main(issue())
        self.assertEqual(repo.calls, [("issue", {"number": 7})])
        built.assert_called_once_with(repo, 7, 3600)

    def test_a_factory_pull_request_closes_its_issue_as_github_reports_it(self):
        cases = (
            (closes(REPO, 7), True),
            (closes(REPO, 8), False),
            (closes(REPO), False),
            (closes("o/other", 7), False),
        )
        for references, expected in cases:
            repo = FakeRepo(REPO)
            repo.answer("pull_request", (5,), {**FACTORY_PR, **references})
            with self.subTest(references=references):
                self.assertIs(build.is_factory_pull_request(repo, 5, 7), expected)


if __name__ == "__main__":
    unittest.main()
