"""Unit tests of read.py: the parse of the reader's Report, for #407, the exit on a session that gives none, for #518,
and the dedupe on the red SHA, the rerun and the read of the failed log. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls the API: a session is the FakeApi of
.github/shared/fake_anthropic.py, and GitHub is a FakeGitHub of .github/shared/. The correction turn, the turn and the
page tests moved to .github/shared/test_sessions.py."""

import json
import os
import unittest
from unittest import mock

import read
import runs
import traveler
from fake_anthropic import FakeApi
from fake_github import Fail, FakeGitHub
from sessions import Session

# The module under test puts .github/shared/ on the path, so this test also runs on its own.
comments = read.comments

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
OPEN_FACTORY_REPAIRS = (
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


def message(text):
    return {"type": "agent.message", "content": [{"type": "text", "text": text}]}


def factory_answer(body=SOUND, kind="factory", **fields):
    return json.dumps({"class": kind, "title": "t", "body": body, "evidence": ["e"], **fields})


def parse_or_error(parse, text):
    """The Report and None, or None and the fault of the ValueError that the parse function raised."""
    try:
        return parse(text), None
    except ValueError as error:
        return None, str(error)


def parse_factory(text):
    return parse_or_error(read.factory_report, text)


def parse_ci(text):
    return parse_or_error(read.ci_report, text)


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
        """The arguments and the body of the one `issue create` of a read."""
        gh = FakeGitHub()
        gh.answer(OPEN_FACTORY_REPAIRS, [])
        for labels in (("repair", "ready", "factory"), ("repair", "ready", "factory", "cleanup")):
            create = ["issue", "create", "-R", "o/r", "--title", answer["title"]]
            for name in labels:
                create += ["--label", name]
            gh.answer((*create, "--body-file", "-"), "https://github.com/o/r/issues/7\n")
        with mock.patch.dict(os.environ, {"GITHUB_OUTPUT": os.devnull}):
            read.finish_factory(gh, "o/r", RUN, STORY, None, answer, SID)
        return next((args, body) for args, body in gh.calls if args[:2] == ("issue", "create"))

    def labels(self, args):
        return [args[i + 1] for i, arg in enumerate(args) if arg == "--label"]

    def test_a_cleanup_answer_takes_the_label_cleanup(self):
        args, _ = self.filed({"class": "factory", "recipe": "cleanup", "title": "t", "body": SOUND, "evidence": []})
        self.assertEqual(self.labels(args), ["repair", "ready", "factory", "cleanup"])

    def test_a_none_answer_keeps_three_labels(self):
        args, _ = self.filed({"class": "dependency", "recipe": "none", "title": "t", "body": "b", "evidence": []})
        self.assertEqual(self.labels(args), ["repair", "ready", "factory"])

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


REPAIRS = (
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
RED = {"id": 9, "head_sha": "abc1234", "html_url": "https://github.com/o/r/actions/runs/9", "run_attempt": 1}


def take_comments(gh, number, sha):
    """The target of comments.py, issue or pull request <number> or the commit <sha>, takes a post, and its list of
    comments holds each one posted."""
    path = comments.path("o/r", number, sha)
    gh.answer_list(path, lambda: [{"body": text} for text in posted(gh, number, sha)])
    gh.answer(("api", "-X", "POST", path, "--input", "-"), "{}")


def posted(gh, number, sha):
    """The body of each comment that comments.py posted on the target."""
    post = ("api", "-X", "POST", comments.path("o/r", number, sha), "--input", "-")
    return [json.loads(text)["body"] for args, text in gh.calls if args == post]


ALSO_RED = ("issue", "comment", "4", "-R", "o/r", "--body", f"The run {RED['html_url']} on `abc1234` is also red.")


def repair(number, state="OPEN", body="Fix it.\n\nSHA abc1234", labels=("repair",)):
    return {"number": number, "state": state, "body": body, "labels": [{"name": name} for name in labels]}


class DedupeTests(unittest.TestCase):
    """already_handled: one repair for each red SHA, as #388 fixed."""

    def handled(self, issues, comments="", gh=None):
        gh = gh or FakeGitHub()
        gh.answer(REPAIRS, issues)
        gh.answer(("api", "repos/o/r/issues/4/comments", "--paginate", "--jq", ".[].body"), comments)
        take_comments(gh, None, "abc1234")
        gh.answer(ALSO_RED, "")
        return read.already_handled(gh, "o/r", None, RED), gh

    def test_an_open_issue_that_holds_the_sha_takes_one_comment(self):
        handled, gh = self.handled([repair(4)])
        self.assertTrue(handled)
        self.assertEqual(gh.count(*ALSO_RED), 1)

    def test_an_open_issue_that_names_the_run_already_takes_no_comment(self):
        handled, gh = self.handled([repair(4)], comments=f"The run {RED['html_url']} on `abc1234` is also red.")
        self.assertTrue(handled)
        self.assertEqual(gh.count(*ALSO_RED), 0)

    def test_a_closed_issue_that_holds_the_sha_ends_the_read_with_no_comment(self):
        handled, gh = self.handled([repair(4, state="CLOSED")])
        self.assertTrue(handled)
        self.assertEqual(gh.count(*ALSO_RED), 0)

    def test_an_open_issue_wins_over_a_closed_one(self):
        handled, gh = self.handled([repair(3, state="CLOSED"), repair(4)])
        self.assertTrue(handled)
        self.assertEqual(gh.count(*ALSO_RED), 1)

    def test_the_oldest_closed_issue_wins_over_a_later_one(self):
        gh = FakeGitHub()
        gh.answer(REPAIRS, [repair(6, "CLOSED"), repair(4, "CLOSED")])
        self.assertEqual(read.issue_for(gh, "o/r", "abc1234")["number"], 4)

    def test_a_factory_repair_holds_no_ci_run_of_its_sha(self):
        handled, gh = self.handled([repair(4, labels=("repair", "factory"))])
        self.assertFalse(handled)
        self.assertEqual(gh.count(*ALSO_RED), 0)

    def test_a_marker_comment_on_the_commit_ends_the_read(self):
        gh = FakeGitHub()
        take_comments(gh, None, "abc1234")
        read.comment(gh, "o/r", None, "abc1234", "No repair.")
        self.assertTrue(self.handled([], gh=gh)[0])

    def test_a_marker_comment_on_the_pull_request_ends_the_read(self):
        gh = FakeGitHub()
        gh.answer(REPAIRS, [])
        take_comments(gh, 8, "abc1234")
        read.comment(gh, "o/r", {"number": 8}, "abc1234", "No repair.")
        self.assertTrue(read.already_handled(gh, "o/r", {"number": 8}, RED))
        gh = FakeGitHub()
        gh.answer(REPAIRS, [])
        take_comments(gh, 8, "abc1234")
        self.assertFalse(read.already_handled(gh, "o/r", {"number": 8}, RED))

    def test_no_issue_and_no_marker_needs_a_repair(self):
        handled, gh = self.handled([repair(4, body="another SHA")])
        self.assertFalse(handled)
        self.assertEqual(gh.count(*ALSO_RED), 0)


class RerunTests(unittest.TestCase):
    RERUN = ("run", "rerun", "9", "-R", "o/r", "--failed")
    READ = ("api", "repos/o/r/actions/runs/9")

    def test_the_rerun_gives_the_new_attempt_once_it_completes(self):
        gh = FakeGitHub()
        gh.answer(self.RERUN, "")
        old = {**RED, "status": "completed", "conclusion": "failure"}
        running = {**RED, "run_attempt": 2, "status": "in_progress"}
        done = {**RED, "run_attempt": 2, "status": "completed", "conclusion": "success"}
        gh.answer(self.READ, old, running, done)
        with mock.patch.object(runs.time, "sleep"):
            self.assertEqual(read.rerun(gh, "o/r", RED, 3600), done)
        self.assertEqual(gh.count(*self.RERUN), 1)
        self.assertEqual(gh.count(*self.READ), 3)

    def test_a_rerun_still_running_at_the_timeout_exits(self):
        gh = FakeGitHub()
        gh.answer(self.RERUN, "")
        gh.answer(self.READ, {**RED, "run_attempt": 2, "status": "in_progress"})
        with self.assertRaises(SystemExit) as stop:
            read.rerun(gh, "o/r", RED, 0)
        self.assertIn("still running", stop.exception.code)
        self.assertEqual(gh.count(*self.READ), 1)

    def test_a_completed_read_of_the_old_attempt_waits(self):
        gh = FakeGitHub()
        gh.answer(self.RERUN, "")
        old = {**RED, "status": "completed", "conclusion": "failure"}
        done = {**RED, "run_attempt": 2, "status": "completed", "conclusion": "failure"}
        gh.answer(self.READ, old, old, done)
        with mock.patch.object(runs.time, "sleep") as sleep:
            self.assertEqual(read.rerun(gh, "o/r", RED, 3600), done)
        self.assertEqual([call.args for call in sleep.call_args_list], [(read.RERUN_POLL,), (read.RERUN_POLL,)])


# The verb of a dispatch, one word on each line, so the grep of #516's `## Done when` matches runs.py alone.
START = (
    "workflow",
    "run",
)


class FlakeTests(unittest.TestCase):
    def test_a_flake_on_a_factory_run_starts_the_story_at_main_once_more(self):
        gh = FakeGitHub()
        dispatch = (*START, runs.WORKFLOW, "-R", "o/r", "--ref", "main", "-f", "issue=12")
        gh.answer(dispatch, "")
        take_comments(gh, 12, "abc1234")
        answer = {"class": "flake", "evidence": ["a line"]}
        read.finish_factory(gh, "o/r", RED, {"number": 12}, {"number": 12, "story": True}, answer, "sesn_1")
        self.assertEqual(gh.count(*dispatch), 1)
        [text] = posted(gh, 12, "abc1234")
        self.assertIn("the reader's class is `flake`", text)


class CommentTests(unittest.TestCase):
    """read.comment: one comment for each red SHA, through comments.py."""

    def test_a_comment_goes_on_the_pull_request_once_for_the_sha(self):
        gh = FakeGitHub()
        take_comments(gh, 8, "abc1234")
        for _ in range(2):
            read.comment(gh, "o/r", {"number": 8}, "abc1234", "No repair.")
        [text] = posted(gh, 8, "abc1234")
        self.assertIn("repair:abc1234", text.splitlines()[0])
        self.assertTrue(text.endswith("\nNo repair."))

    def test_a_comment_on_main_goes_on_the_commit(self):
        gh = FakeGitHub()
        take_comments(gh, None, "abc1234")
        read.comment(gh, "o/r", None, "abc1234", "No repair.")
        self.assertEqual(len(posted(gh, None, "abc1234")), 1)

    def test_a_marker_of_another_sha_takes_a_comment(self):
        gh = FakeGitHub()
        take_comments(gh, 8, "abc1234")
        read.comment(gh, "o/r", {"number": 8}, "def5678", "No repair.")
        read.comment(gh, "o/r", {"number": 8}, "abc1234", "No repair.")
        self.assertEqual(len(posted(gh, 8, "abc1234")), 2)


class KillSwitchTests(unittest.TestCase):
    """REPAIR_ENABLED binds every event but a run by hand, workflow_dispatch. repair.yml holds one other event; each
    other name stands for it here."""

    def run_main(self, env):
        gh = FakeGitHub()
        gh.answer(("api", "repos/o/r/actions/runs/9"), {**RED, "conclusion": "success"})
        with (
            mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": "o/r", **env}, clear=True),
            mock.patch.object(read.sys, "argv", ["read.py", "9"]),
        ):
            read.main(gh)
        return gh.args()

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
                self.assertEqual(self.run_main(env), [("api", "repos/o/r/actions/runs/9")])


class FailedLogTests(unittest.TestCase):
    VIEW = ("run", "view", "9", "-R", "o/r", "--attempt", "1", "--log-failed")

    def log(self, output):
        gh = FakeGitHub()
        gh.answer(self.VIEW, output)
        return read.failed_log(gh, "o/r", RED)

    def test_the_log_of_a_passing_call(self):
        self.assertEqual(self.log("the log\n"), "the log\n")

    def test_exit_1_with_output_keeps_the_output(self):
        self.assertEqual(self.log(Fail(stdout="the other jobs\n", stderr="log not found")), "the other jobs\n")

    def test_exit_1_with_no_output_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.log(Fail(stdout=" \n", stderr="log not found"))
        self.assertIn("log not found", stop.exception.code)

    def test_another_exit_code_exits_with_output_or_none(self):
        with self.assertRaises(SystemExit):
            self.log(Fail(stdout="partial\n", stderr="killed", returncode=2))


if __name__ == "__main__":
    unittest.main()
