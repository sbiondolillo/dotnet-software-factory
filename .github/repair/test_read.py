"""Unit tests of read.py: the parse of the reader's Report, for #407, the exit on a session that gives none, for #518,
and the dedupe on the red SHA, the rerun and the read of the failed log. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls the API: a session is the FakeApi of
.github/shared/fake_anthropic.py, and the repo is a FakeRepo of .github/shared/. The correction turn, the turn and the
page tests moved to .github/shared/test_sessions.py. The exit code 1 of `gh run view --log-failed` is a fact of
repo.py, and test_repo.py tests it."""

import json
import os
import unittest
from unittest import mock

import read
import runs
import traveler
from fake_anthropic import FakeApi
from fake_repo import Fail, FakeRepo
from sessions import Session

SID = "sesn_1"
GOOD = '{"class": "code", "title": "t", "body": "b", "evidence": ["e"]}'
# The slip of run 41338664688: a stray `]` closed the body string.
STRAY = '{"class": "code", "title": "t", "body": "b"], "evidence": []}'
DELIMITER = "Expecting ',' delimiter"
FACTORY_FILES = ("factory", "dependency")
SCOPE = "## Scope\n\n- `.github/workflows/factory.yml`\n\n"
CHECK = "## Check\n\n```sh\ngrep -c 'lock: claude' .github/workflows/factory.yml\n```\n"
SOUND = f"Cleanup. Read the right key.\n\n{SCOPE}{CHECK}"
RUN = {"id": 1, "head_sha": "abc", "html_url": "https://run"}
STORY = {"number": 451, "title": "t"}


def message(text):
    return {"type": "agent.message", "content": [{"type": "text", "text": text}]}


def factory_answer(body=SOUND, kind="factory", **fields):
    return json.dumps({"class": kind, "title": "t", "body": body, "evidence": ["e"], **fields})


def fault_of(parse, text):
    """The Report and None, or None and the fault of the ValueError that the parse function raised."""
    try:
        return parse(text), None
    except ValueError as error:
        return None, str(error)


def parse_factory(text):
    return fault_of(read.factory_report, text)


def parse_ci(text):
    return fault_of(read.ci_report, text)


class ParseTests(unittest.TestCase):
    def test_a_valid_object_parses(self):
        answer, error = parse_ci(GOOD)
        self.assertIsNone(error)
        self.assertEqual(answer, {"class": "code", "title": "t", "body": "b", "evidence": ["e"]})

    def test_the_stray_bracket_gives_the_parser_error(self):
        answer, error = parse_ci(STRAY)
        self.assertIsNone(answer)
        self.assertTrue(error.startswith("JSONDecodeError: "), error)
        self.assertIn(DELIMITER, error)

    def test_a_missing_field_gives_the_parser_error(self):
        self.assertEqual(parse_ci('{"class": "code", "title": "t"}'), (None, "KeyError: 'body'"))


class RecipeTests(unittest.TestCase):
    def test_a_cleanup_answer_with_a_sound_body_parses(self):
        answer, error = parse_factory(factory_answer(recipe="cleanup"))
        self.assertIsNone(error)
        self.assertEqual(answer["recipe"], "cleanup")

    def test_none_parses_with_no_scope_and_no_check(self):
        answer, error = parse_factory(factory_answer("A fix in `src/`.", recipe="none"))
        self.assertIsNone(error)
        self.assertEqual(answer["recipe"], "none")

    def test_a_class_that_files_needs_a_known_recipe(self):
        for kind in FACTORY_FILES:
            for fields, shown in (
                ({}, "None"),
                ({"recipe": "behavior"}, "'behavior'"),
                ({"recipe": ["none"]}, "['none']"),
            ):
                with self.subTest(kind=kind, fields=fields):
                    answer, error = parse_factory(factory_answer(kind=kind, **fields))
                    self.assertIsNone(answer)
                    self.assertEqual(error, f"AssertionError: the recipe {shown} is not one of cleanup, none")

    def test_code_and_flake_on_a_factory_read_need_no_recipe(self):
        for kind, body in (("code", "the reason"), ("flake", "")):
            with self.subTest(kind=kind):
                answer, error = parse_factory(factory_answer(body, kind))
                self.assertIsNone(error)
                self.assertEqual(answer["class"], kind)

    def test_a_ci_answer_needs_no_recipe(self):
        answer, error = parse_ci(GOOD)
        self.assertIsNone(error)
        self.assertNotIn("recipe", answer)

    def test_the_shapes(self):
        self.assertIn('"recipe": "cleanup" or "none"', read.FACTORY_REPORT)
        self.assertNotIn("recipe", read.REPORT)


class CleanupBodyTests(unittest.TestCase):
    """The cases agree with tests/Specs/Unit/CleanupBodyTests.cs for the C# that reads the body at run time."""

    def faults(self, body):
        answer, error = parse_factory(factory_answer(body, recipe="cleanup"))
        self.assertIsNone(answer)
        return error

    def test_a_fault_of_the_body_is_an_assertion_message(self):
        unclosed = "## Check\n\n```sh\ntrue\n"
        for name, body, fault in (
            ("no scope", CHECK, "the cleanup body has no `## Scope` heading"),
            ("empty scope", f"## Scope\n\n  \n\n{CHECK}", "`## Scope` holds no path"),
            ("a fence alone", f"## Scope\n\n```\n```\n\n{CHECK}", "`## Scope` holds no path"),
            ("no check", SCOPE, "the cleanup body has no `## Check` heading"),
            ("no block", f"{SCOPE}## Check\n\ntrue\n", "`## Check` holds no closed fenced block with a command"),
            ("a block after the section", f"{SCOPE}## Check\n\n## Notes\n```sh\ntrue\n```\n", "`## Check` holds no"),
            ("blank block", f"{SCOPE}## Check\n\n```sh\n\n```\n", "`## Check` holds no closed fenced block"),
            ("unclosed fence", SCOPE + unclosed, "`## Check` holds no closed fenced block with a command"),
        ):
            with self.subTest(name):
                error = self.faults(body)
                self.assertTrue(error.startswith("AssertionError: "), error)
                self.assertIn(fault, error)

    def test_the_forms_the_csharp_reads_parse(self):
        for name, body in (
            ("a list item", f"## Scope\n- a.yml\n{CHECK}"),
            ("a star item", f"## Scope\n* a.yml\n{CHECK}"),
            ("a backticked path", f"## Scope\n`a.yml`\n{CHECK}"),
            ("a bare path", f"## Scope\na.yml\n{CHECK}"),
            ("a bare fence", f"{SCOPE}## Check\n```\ntrue\n```\n"),
            ("a tilde fence", f"{SCOPE}## Check\n~~~\ntrue\n~~~\n"),
            ("a tilde fence with an info string", f"{SCOPE}## Check\n~~~sh\ntrue\n~~~\n"),
            ("a longer closing fence", f"{SCOPE}## Check\n```sh\ntrue\n`````\n"),
            ("a carriage return", "## Scope\r\na.yml\r\n## Check\r\n```sh\r\ntrue\r\n```\r\n"),
            ("the check first", f"{CHECK}\n{SCOPE}"),
        ):
            with self.subTest(name):
                self.assertIsNone(parse_factory(factory_answer(body, recipe="cleanup"))[1])

    def test_a_heading_inside_a_fence_does_not_end_a_section(self):
        body = f"{SCOPE}## Check\n```sh\n## not a heading\ntrue\n```\n"
        self.assertIsNone(parse_factory(factory_answer(body, recipe="cleanup"))[1])
        self.assertEqual(read.first_block(read.section(body, "## Check")), "## not a heading\ntrue")

    def test_a_heading_inside_a_fence_is_no_section(self):
        body = f"## Notes\n```\n## Scope\na.yml\n```\n{CHECK}"
        self.assertIn("no `## Scope` heading", self.faults(body))

    def test_a_section_ends_at_the_next_heading(self):
        self.assertIn("`## Scope` holds no path", self.faults(f"## Scope\n## Notes\na.yml\n{CHECK}"))

    def test_the_first_block_wins(self):
        lines = read.section("## Check\n```sh\none\n```\n```sh\ntwo\n```\n", "## Check")
        self.assertEqual(read.first_block(lines), "one")

    def test_a_fence_with_a_backtick_after_it_opens_nothing(self):
        self.assertIsNone(read.opens_fence("``` a`b"))
        self.assertEqual(read.opens_fence("~~~ a~b"), "~~~")


class FactoryPromptTests(unittest.TestCase):
    def test_the_factory_prompt_names_the_sandbox_tools_and_the_recipes(self):
        prompt = read.factory_reader_prompt("o/r", RUN, STORY, "records")
        for tool in ("grep", "sed", "awk", "sha256sum", "git", "node", "dotnet", "jq", "python", "python3", "yq"):
            self.assertIn(f"`{tool}`", prompt)
        self.assertIn("`recipe` is `none`", prompt)
        self.assertIn("`recipe` is `cleanup`", prompt)
        self.assertIn("exits with a code other than 0, 126 and 127", prompt)
        self.assertNotIn("Use the recipe with no label", prompt)
        self.assertIn(read.FACTORY_REPORT, prompt)

    def test_the_ci_prompt_stays(self):
        prompt = read.reader_prompt("o/r", RUN, "`repair/1`", "log")
        self.assertIn("Use the recipe with no label, since a fix to a red run is a bug fix.", prompt)
        self.assertNotIn("recipe`", prompt)


class FinishFactoryTests(unittest.TestCase):
    def filed(self, answer):
        """The labels and the body of the one issue a read creates."""
        repo = FakeRepo()
        repo.answer("open_issues_with_labels", (("repair", "factory"),), [])
        repo.answer("create_issue", (answer["title"], mock.ANY, mock.ANY), "https://github.com/o/r/issues/7")
        with mock.patch.dict(os.environ, {"GITHUB_OUTPUT": os.devnull}):
            read.finish_factory(repo, RUN, STORY, None, answer, SID)
        [created] = repo.calls_of("create_issue")
        return list(created["labels"]), created["body"]

    def test_a_cleanup_answer_takes_the_label_cleanup(self):
        labels, _ = self.filed({"class": "factory", "recipe": "cleanup", "title": "t", "body": SOUND, "evidence": []})
        self.assertEqual(labels, ["repair", "ready", "factory", "cleanup"])

    def test_a_none_answer_keeps_three_labels(self):
        labels, _ = self.filed({"class": "dependency", "recipe": "none", "title": "t", "body": "b", "evidence": []})
        self.assertEqual(labels, ["repair", "ready", "factory"])

    def test_the_run_section_ends_a_last_section_of_the_filed_body(self):
        for name, body, scope in (
            ("the check last", SOUND, ["- `.github/workflows/factory.yml`"]),
            ("the scope last", f"{CHECK}\n## Scope\n\na.yml", ["a.yml"]),
        ):
            with self.subTest(name):
                _, filed = self.filed({"class": "factory", "recipe": "cleanup", "title": "t", "body": body})
                read.check_cleanup_body(filed)
                self.assertIn("## Run", filed)
                self.assertEqual([line for line in read.section(filed, "## Scope") if line.strip()], scope)

    def test_the_filed_body_ends_with_a_traveler_that_names_the_story(self):
        _, filed = self.filed({"class": "dependency", "recipe": "none", "title": "t", "body": "b", "evidence": []})
        expected = traveler.Traveler(
            run=RUN["html_url"], target="a new branch from the head of `main`", sha="abc", session=SID, story=451
        )
        self.assertEqual(traveler.parse(filed), expected)
        self.assertTrue(filed.endswith(traveler.render(expected)))

    def test_the_run_section_closes_no_fence(self):
        _, filed = self.filed(
            {"class": "factory", "recipe": "cleanup", "title": "t", "body": f"{SCOPE}## Check\n```sh\ntrue"}
        )
        self.assertIsNone(read.first_block(read.section(filed, "## Check")))


class ReadReportTests(unittest.TestCase):
    """read_report: the Report of the reader's session, or the exit with the reason and the text."""

    def read(self, listings, story=None):
        session = Session(FakeApi(listings), SID)
        with mock.patch("sessions.time.sleep"), mock.patch("builtins.print"):
            return read.read_report(session, story, 60)

    def test_a_good_first_message_is_the_report(self):
        self.assertEqual(self.read([[message(GOOD)]])["class"], "code")

    def test_a_factory_read_parses_with_the_factory_shape(self):
        answer = self.read([[message(factory_answer(recipe="cleanup"))]], story=STORY)
        self.assertEqual(answer["recipe"], "cleanup")

    def test_a_session_error_exits_with_the_reason_and_the_text(self):
        refused = [{"type": "session.status_idle", "stop_details": {"type": "refusal"}}, message("I will not")]
        with self.assertRaises(SystemExit) as stop:
            self.read([refused])
        self.assertEqual(stop.exception.code, f"repair: session {SID} gave no Report: refused: I will not")

    def test_an_api_error_in_the_read_exits_with_the_session_id(self):
        session = Session(FakeApi([[message(GOOD)]], failing=("events",)), SID)
        with self.assertRaises(SystemExit) as stop, mock.patch("builtins.print"):
            read.read_report(session, None, 60)
        reason = f"the API failed: GET /v1/sessions/{SID}/events?order=desc&limit=100 -> 500: overloaded"
        self.assertEqual(stop.exception.code, f"repair: session {SID} gave no Report: {reason}: ")

    def test_a_cleanup_fault_reaches_the_reader_by_the_correction_turn(self):
        first = [message(factory_answer(f"{SCOPE}## Check\n\nNo block.\n", recipe="cleanup"))]
        first.append({"type": "user.message", "id": "m1"})
        second = [
            {"type": "session.status_idle", "stop_details": {"type": "end_turn"}},
            message(factory_answer(recipe="cleanup")),
            {"type": "user.message", "id": "m2"},
        ]
        api = FakeApi([first, second])
        with mock.patch("builtins.print"):
            answer = read.read_report(Session(api, SID), STORY, 60)
        self.assertEqual(answer["recipe"], "cleanup")
        sent = api.posts[0][1]["events"][0]["content"][0]["text"]
        self.assertIn("`## Check` holds no closed fenced block", sent)
        self.assertIn(read.FACTORY_REPORT, sent)


RED = {"id": 9, "head_sha": "abc1234", "html_url": "https://github.com/o/r/actions/runs/9", "run_attempt": 1}


def take_comments(repo, number, sha):
    """The target of comments.py, issue or pull request <number> or the commit <sha>, takes a post, and its list of
    comments holds each one posted."""
    repo.answer("comments", (number, sha), lambda: [{"body": text} for text in posted(repo, number, sha)])
    repo.answer("post_comment", (number, mock.ANY, sha), None)


def posted(repo, number, sha):
    """The body of each comment that comments.py posted on the target. A number names the target before a SHA, as
    Repo reads it, so each comment on the issue or pull request <number> is on its list, whatever its SHA."""
    return [
        call["body"]
        for call in repo.calls_of("post_comment")
        if call["number"] == number and (number is not None or call["sha"] == sha)
    ]


ALSO_RED = {"number": 4, "body": f"The run {RED['html_url']} on `abc1234` is also red."}


def repair(number, state="OPEN", body="Fix it.\n\nSHA abc1234", labels=("repair",)):
    return {"number": number, "state": state, "body": body, "labels": [{"name": name} for name in labels]}


class DedupeTests(unittest.TestCase):
    """already_handled: one repair for each red SHA, as #388 fixed."""

    def handled(self, issues, comments=(), repo=None):
        repo = repo or FakeRepo()
        repo.answer("issues_with_label", ("repair",), issues)
        repo.answer("comments", (4,), [{"body": body} for body in comments])
        take_comments(repo, None, "abc1234")
        repo.answer("post_comment", tuple(ALSO_RED.values()), None)
        return read.already_handled(repo, None, RED), repo

    def test_an_open_issue_that_holds_the_sha_takes_one_comment(self):
        handled, repo = self.handled([repair(4)])
        self.assertTrue(handled)
        self.assertEqual(posted(repo, 4, None), [ALSO_RED["body"]])

    def test_an_open_issue_that_names_the_run_already_takes_no_comment(self):
        handled, repo = self.handled(
            [repair(4)], comments=["first", f"The run {RED['html_url']} on `abc1234` is also red."]
        )
        self.assertTrue(handled)
        self.assertEqual(posted(repo, 4, None), [])

    def test_a_comment_with_no_body_names_no_run(self):
        handled, repo = self.handled([repair(4)], comments=[None, ""])
        self.assertTrue(handled)
        self.assertEqual(posted(repo, 4, None), [ALSO_RED["body"]])

    def test_a_closed_issue_that_holds_the_sha_ends_the_read_with_no_comment(self):
        handled, repo = self.handled([repair(4, state="CLOSED")])
        self.assertTrue(handled)
        self.assertEqual(posted(repo, 4, None), [])

    def test_an_open_issue_wins_over_a_closed_one(self):
        handled, repo = self.handled([repair(3, state="CLOSED"), repair(4)])
        self.assertTrue(handled)
        self.assertEqual(posted(repo, 4, None), [ALSO_RED["body"]])

    def test_the_oldest_closed_issue_wins_over_a_later_one(self):
        repo = FakeRepo()
        repo.answer("issues_with_label", ("repair",), [repair(6, "CLOSED"), repair(4, "CLOSED")])
        self.assertEqual(read.issue_for(repo, "abc1234")["number"], 4)

    def test_a_factory_repair_holds_no_ci_run_of_its_sha(self):
        handled, repo = self.handled([repair(4, labels=("repair", "factory"))])
        self.assertFalse(handled)
        self.assertEqual(posted(repo, 4, None), [])

    def test_a_marker_comment_on_the_commit_ends_the_read(self):
        repo = FakeRepo()
        take_comments(repo, None, "abc1234")
        read.comment(repo, None, "abc1234", "No repair.")
        self.assertTrue(self.handled([], repo=repo)[0])

    def test_a_marker_comment_on_the_pull_request_ends_the_read(self):
        repo = FakeRepo()
        repo.answer("issues_with_label", ("repair",), [])
        take_comments(repo, 8, "abc1234")
        read.comment(repo, {"number": 8}, "abc1234", "No repair.")
        self.assertTrue(read.already_handled(repo, {"number": 8}, RED))
        repo = FakeRepo()
        repo.answer("issues_with_label", ("repair",), [])
        take_comments(repo, 8, "abc1234")
        self.assertFalse(read.already_handled(repo, {"number": 8}, RED))

    def test_a_second_red_factory_run_comments_on_the_open_issue_and_on_its_commit(self):
        repo = FakeRepo()
        repo.answer("issues_with_label", ("repair",), [repair(4, labels=("repair", "factory"))])
        repo.answer("comments", (4,), [])
        repo.answer("post_comment", (4, mock.ANY), None)
        repo.answer("post_comment", (None, mock.ANY, "abc1234"), None)
        self.assertTrue(read.already_handled(repo, {"number": 451, "story": True}, RED))
        self.assertEqual(posted(repo, 4, None), [ALSO_RED["body"]])
        self.assertEqual(
            posted(repo, None, "abc1234"),
            [
                f"The factory run {RED['html_url']} on story #451 is red. The factory repair #4 is open, so no second "
                "repair."
            ],
        )

    def test_no_issue_and_no_marker_needs_a_repair(self):
        handled, repo = self.handled([repair(4, body="another SHA")])
        self.assertFalse(handled)
        self.assertEqual(posted(repo, 4, None), [])


class RerunTests(unittest.TestCase):
    def fake(self, *reads):
        repo = FakeRepo()
        repo.answer("rerun_failed", (9,), None)
        repo.answer("workflow_run", (9,), *reads)
        return repo

    def test_the_rerun_gives_the_new_attempt_once_it_completes(self):
        old = {**RED, "status": "completed", "conclusion": "failure"}
        running = {**RED, "run_attempt": 2, "status": "in_progress"}
        done = {**RED, "run_attempt": 2, "status": "completed", "conclusion": "success"}
        repo = self.fake(old, running, done)
        with mock.patch.object(runs.time, "sleep"):
            self.assertEqual(read.rerun(repo, RED, 3600), done)
        self.assertEqual(repo.calls_of("rerun_failed"), [{"run_id": 9}])
        self.assertEqual(repo.calls_of("workflow_run"), [{"run_id": 9}] * 3)

    def test_a_rerun_still_running_at_the_timeout_exits(self):
        repo = self.fake({**RED, "run_attempt": 2, "status": "in_progress"})
        with self.assertRaises(SystemExit) as stop:
            read.rerun(repo, RED, 0)
        self.assertIn("still running", stop.exception.code)
        self.assertEqual(len(repo.calls_of("workflow_run")), 1)

    def test_a_completed_read_of_the_old_attempt_waits(self):
        old = {**RED, "status": "completed", "conclusion": "failure"}
        done = {**RED, "run_attempt": 2, "status": "completed", "conclusion": "failure"}
        repo = self.fake(old, old, done)
        with mock.patch.object(runs.time, "sleep") as sleep:
            self.assertEqual(read.rerun(repo, RED, 3600), done)
        self.assertEqual([call.args for call in sleep.call_args_list], [(read.RERUN_POLL,), (read.RERUN_POLL,)])


class FlakeTests(unittest.TestCase):
    def test_a_flake_on_a_factory_run_starts_the_story_at_main_once_more(self):
        repo = FakeRepo()
        dispatch = {"workflow": runs.WORKFLOW, "ref": "main", "inputs": {"issue": 12}}
        repo.answer("dispatch_workflow", tuple(dispatch.values()), None)
        take_comments(repo, 12, "abc1234")
        answer = {"class": "flake", "evidence": ["a line"]}
        read.finish_factory(repo, RED, {"number": 12}, {"number": 12, "story": True}, answer, "sesn_1")
        self.assertEqual(repo.calls_of("dispatch_workflow"), [dispatch])
        [text] = posted(repo, 12, "abc1234")
        self.assertIn("the reader's class is `flake`", text)


class CommentTests(unittest.TestCase):
    """read.comment: one comment for each red SHA, through comments.py."""

    def test_a_comment_goes_on_the_pull_request_once_for_the_sha(self):
        repo = FakeRepo()
        take_comments(repo, 8, "abc1234")
        for _ in range(2):
            read.comment(repo, {"number": 8}, "abc1234", "No repair.")
        [text] = posted(repo, 8, "abc1234")
        self.assertIn("repair:abc1234", text.splitlines()[0])
        self.assertTrue(text.endswith("\nNo repair."))

    def test_a_comment_on_main_goes_on_the_commit(self):
        repo = FakeRepo()
        take_comments(repo, None, "abc1234")
        read.comment(repo, None, "abc1234", "No repair.")
        self.assertEqual(len(posted(repo, None, "abc1234")), 1)

    def test_a_marker_of_another_sha_takes_a_comment(self):
        repo = FakeRepo()
        take_comments(repo, 8, "abc1234")
        take_comments(repo, 8, "def5678")
        read.comment(repo, {"number": 8}, "def5678", "No repair.")
        read.comment(repo, {"number": 8}, "abc1234", "No repair.")
        self.assertEqual(len(posted(repo, 8, "abc1234")), 2)


class KillSwitchTests(unittest.TestCase):
    """REPAIR_ENABLED binds every event but a run by hand, workflow_dispatch. repair.yml holds one other event; each
    other name stands for it here."""

    def run_main(self, env):
        repo = FakeRepo()
        repo.answer("workflow_run", ("9",), {**RED, "conclusion": "success"})
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": "o/r", **env}, clear=True),
            mock.patch.object(read.sys, "argv", ["read.py", "9"]),
        ):
            read.main(repo)
        return repo.calls

    def test_with_the_switch_off_an_event_reads_no_run(self):
        for env in (
            {"GITHUB_EVENT_NAME": "push"},
            {"GITHUB_EVENT_NAME": "schedule", "REPAIR_ENABLED": "1"},
            {},
        ):
            with self.subTest(env=env):
                self.assertEqual(self.run_main(env), [])

    def test_a_run_by_hand_or_the_switch_on_reads_the_run(self):
        for env in (
            {"GITHUB_EVENT_NAME": "workflow_dispatch"},
            {"GITHUB_EVENT_NAME": "push", "REPAIR_ENABLED": "true"},
        ):
            with self.subTest(env=env):
                self.assertEqual(self.run_main(env), [("workflow_run", {"run_id": "9"})])


class FailedLogTests(unittest.TestCase):
    def log(self, output):
        repo = FakeRepo()
        repo.answer("failed_log", (9, 1), output)
        return read.failed_log(repo, RED)

    def test_the_log_of_the_attempt(self):
        with mock.patch("builtins.print") as printed:
            self.assertEqual(self.log(("the log\n", None)), "the log\n")
        printed.assert_not_called()

    def test_a_kept_log_of_a_failed_read_is_named_in_the_job_log(self):
        with mock.patch("builtins.print") as printed:
            self.assertEqual(self.log(("the other jobs\n", "log not found\n")), "the other jobs\n")
        printed.assert_called_once_with("repair: gh run view 9 exited 1: log not found; kept stdout")

    def test_a_failed_read_exits_with_its_text(self):
        with self.assertRaises(SystemExit) as stop:
            self.log(Fail(stdout=" \n", stderr="log not found"))
        self.assertEqual(stop.exception.code, "repair: gh run view 9 failed: log not found")


class RunCommentsTests(unittest.TestCase):
    """run_comments: the comments on the story inside the time of the run, each under its time and its author."""

    RUN = {"run_started_at": "2026-11-11T10:00:00Z", "updated_at": "2026-11-11T11:00:00Z"}

    def test_the_comments_inside_the_run_in_order(self):
        repo = FakeRepo()
        repo.answer(
            "comments",
            (451,),
            [
                {"created_at": "2026-11-11T09:59:59Z", "user": {"login": "a"}, "body": "before"},
                {"created_at": "2026-11-11T10:00:00Z", "user": {"login": "owner"}, "body": "one\ntwo"},
                {"created_at": "2026-11-11T10:30:00Z", "user": None, "body": None},
                {"created_at": "2026-11-11T11:00:00Z", "user": {"login": "owner"}, "body": ""},
                {"created_at": "2026-11-11T11:00:01Z", "user": {"login": "b"}, "body": "after"},
            ],
        )
        self.assertEqual(
            read.run_comments(repo, STORY, self.RUN),
            "### 2026-11-11T10:00:00Z, owner\n\none\ntwo\n\n"
            "### 2026-11-11T10:30:00Z, None\n\nNone\n\n"
            "### 2026-11-11T11:00:00Z, owner\n\n",
        )


class CommentsOnTests(unittest.TestCase):
    def test_each_body_and_a_line_feed(self):
        repo = FakeRepo()
        repo.answer("comments", (4,), [{"body": "a\nb"}, {"body": None}, {"body": ""}, {"body": "c"}])
        self.assertEqual(read.comments_on(repo, {"number": 4}), "a\nb\n\n\nc\n")


if __name__ == "__main__":
    unittest.main()
