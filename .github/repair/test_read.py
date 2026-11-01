"""Unit tests of the parse of the reader's answer and of its one correction turn in read.py, for #407. The build job
of ci.yml runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls the API:
each one replaces read.anthropic and read.wait_idle, and time.sleep where the wait polls."""

import json
import os
import unittest
from unittest import mock

import read

SID = "sesn_1"
FILES = ("code", "dependency")
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


OLD_TURN = [
    {"type": "session.status_idle", "stop_details": {"type": "end_turn"}},
    {"type": "user.message", "id": "m1"},
]
NEW_TURN = [
    {"type": "session.status_idle", "stop_details": {"type": "end_turn"}},
    {"type": "user.message", "id": "m2"},
] + OLD_TURN


class FakeApi:
    """The API of one session. A POST records its body. A GET of the events gives the next listing of `listings`,
    newest first, and the last one again when they run out. A listing that is a dict is the whole page, with its
    `next_page`. A GET of the session gives `status`, and `posted` instead once a POST came."""

    def __init__(self, listings, status="idle", posted=None):
        self.posts = []
        self.listings = list(listings)
        self.status = status
        self.posted = posted
        self.reads = 0
        self.paths = []

    def __call__(self, method, path, body=None):
        if method == "POST":
            self.posts.append((path, body))
            if self.posted is not None:
                self.status = self.posted
            return {}
        if path == f"/v1/sessions/{SID}":
            return {"status": self.status}
        self.reads += 1
        self.paths.append(path)
        listing = self.listings.pop(0) if len(self.listings) > 1 else self.listings[0]
        return listing if isinstance(listing, dict) else {"data": listing}


def message(text):
    return {"type": "agent.message", "content": [{"type": "text", "text": text}]}


def factory_answer(body=SOUND, kind="factory", **fields):
    return json.dumps({"class": kind, "title": "t", "body": body, "evidence": ["e"], **fields})


def parse_factory(text):
    return read.parse_or_error(text, read.FACTORY_CLASSES, FACTORY_FILES)


class ParseTests(unittest.TestCase):
    def test_a_valid_object_parses(self):
        answer, error = read.parse_or_error(GOOD, read.CLASSES, FILES)
        self.assertIsNone(error)
        self.assertEqual(answer, {"class": "code", "title": "t", "body": "b", "evidence": ["e"]})

    def test_the_stray_bracket_gives_the_parser_error(self):
        answer, error = read.parse_or_error(STRAY, read.CLASSES, FILES)
        self.assertIsNone(answer)
        self.assertTrue(error.startswith("JSONDecodeError: "), error)
        self.assertIn(DELIMITER, error)


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
        answer, error = read.parse_or_error(GOOD, read.CLASSES, FILES)
        self.assertIsNone(error)
        self.assertNotIn("recipe", answer)

    def test_the_contracts(self):
        self.assertIn('"recipe": "cleanup" or "none"', read.FACTORY_CONTRACT)
        self.assertNotIn("recipe", read.CONTRACT)


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

    def test_a_check_that_runs_an_absent_tool_fails(self):
        for command in (
            "jq .x f",
            "  jq .x f",
            "python3 -c 'print(1)'",
            "python -c 'print(1)'",
            "yq .x f",
            "cat f | jq .x",
            "true && jq .x f",
            "true; yq .x f",
            "! jq -e .x f",
            'test "$(jq .x f)" = y',
            "test `jq .x f` = y",
            "if jq .x f; then true; fi",
            "/usr/bin/jq .x f",
            "A=1 jq .x f",
            "env jq .x f",
            "{ jq .x f; }",
            "case a in a) jq .x f;; esac",
            "eval jq .x f",
            "timeout 5 jq .x f",
            "echo f | xargs -r jq .x",
            "2>/dev/null jq .x f",
            "true 2>&1 | jq .x",
            "command -v jq",
            "type jq",
            'test "$(jq -r ".a" f)" = 1',
            "cat <<X | jq .x\nnot a command\nX",
            "cat f |\\\n  jq .x",
        ):
            with self.subTest(command):
                error = self.faults(f"{SCOPE}## Check\n```sh\n{command}\n```\n")
                self.assertRegex(
                    error, "`## Check` runs `(jq|python3?|yq)`, which the image of `sandbox/Dockerfile` lacks"
                )

    def test_a_check_that_only_names_a_tool_parses(self):
        for command in (
            "grep -c foo f",
            "grep -c jq f",
            "grep -c 'cat f | jq .x' f",
            'grep -c "; python3 x" f',
            "grep -c x\\|jq f",
            "test -f jq",
            "grep -c if jq f",
            "grep -q do python3 f",
            "echo do jq",
            'test ! jq = "$x"',
            'grep -c "`jq .x`" f'.replace("`", "\\`"),
            "grep -c 'a' \"$(grep -c 'x|jq' f)\"",
            "cat <<X\njq is absent\nX",
            "node <<'X'\npython is not a tool of this script\nX",
            "grep -c foo jq.json",
            "jqlang f",
            "./python-check.sh",
            "python/run.sh",
            "true # then jq .x f",
            "node -e 'require(\"./f.json\")'",
        ):
            with self.subTest(command):
                self.assertIsNone(
                    parse_factory(factory_answer(f"{SCOPE}## Check\n```sh\n{command}\n```\n", recipe="cleanup"))[1]
                )


class FactoryPromptTests(unittest.TestCase):
    def test_the_factory_prompt_names_the_sandbox_tools_and_the_recipes(self):
        prompt = read.factory_reader_prompt("o/r", RUN, STORY, "records")
        for tool in ("grep", "sed", "awk", "sha256sum", "git", "node", "dotnet", "jq", "python3", "yq"):
            self.assertIn(f"`{tool}`", prompt)
        self.assertIn("`recipe` is `none`", prompt)
        self.assertIn("`recipe` is `cleanup`", prompt)
        self.assertIn("exits with a code other than 0, 126 and 127", prompt)
        self.assertNotIn("Use the recipe with no label", prompt)
        self.assertIn(read.FACTORY_CONTRACT, prompt)

    def test_the_ci_prompt_stays(self):
        prompt = read.reader_prompt("o/r", RUN, "`repair/1`", "log")
        self.assertIn("Use the recipe with no label, since a fix to a red run is a bug fix.", prompt)
        self.assertNotIn("recipe`", prompt)


class FinishFactoryTests(unittest.TestCase):
    def filed(self, answer):
        """The arguments and the body of the one `issue create` of a read, with `gh` replaced."""
        calls = []

        def gh(*args, input_text=None):
            calls.append((args, input_text))
            return "[]" if args[:2] == ("issue", "list") else "https://github.com/o/r/issues/7\n"

        with mock.patch.object(read, "gh", gh), mock.patch.dict(os.environ, {"GITHUB_OUTPUT": os.devnull}):
            read.finish_factory("o/r", RUN, STORY, None, answer, SID)
        return next((args, body) for args, body in calls if args[:2] == ("issue", "create"))

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

    def test_the_run_section_closes_no_fence(self):
        _, filed = self.filed(
            {"class": "factory", "recipe": "cleanup", "title": "t", "body": f"{SCOPE}## Check\n```sh\ntrue"}
        )
        self.assertIsNone(read.first_block(read.section(filed, "## Check")))


class CorrectionTests(unittest.TestCase):
    def read_answer(self, texts):
        api = FakeApi([OLD_TURN, NEW_TURN])
        answers = [(text, {"type": "end_turn"}) for text in texts]
        with (
            mock.patch.object(read, "anthropic", api),
            mock.patch.object(read, "wait_idle") as wait_idle,
            mock.patch.object(read, "final_answer", side_effect=answers),
        ):
            try:
                return read.read_answer(SID, read.CLASSES, FILES, read.CONTRACT, 60), api, None
            except SystemExit as exit:
                return None, api, str(exit.code)
            finally:
                self.assertEqual(wait_idle.call_count, len(texts) - 1)

    def test_a_bad_answer_gets_one_correction_turn_in_the_same_session(self):
        answer, api, _ = self.read_answer([STRAY, GOOD])
        self.assertEqual(answer["title"], "t")
        self.assertEqual(answer["evidence"], ["e"])
        self.assertEqual(len(api.posts), 1)
        path, body = api.posts[0]
        self.assertEqual(path, f"/v1/sessions/{SID}/events")
        self.assertEqual(len(body["events"]), 1)
        event = body["events"][0]
        self.assertEqual(event["type"], "user.message")
        self.assertEqual([c["type"] for c in event["content"]], ["text"])
        self.assertIn(DELIMITER, event["content"][0]["text"])
        self.assertIn(read.CONTRACT, event["content"][0]["text"])
        json.dumps(body)

    def test_two_bad_answers_exit_with_both_errors(self):
        second = '{"class": "code", "title": "t"}'
        answer, api, message = self.read_answer([STRAY, second])
        self.assertIsNone(answer)
        self.assertEqual(len(api.posts), 1)
        self.assertIn(DELIMITER, message)
        self.assertIn("KeyError: 'body'", message)

    def test_a_cleanup_fault_reaches_the_reader_by_the_correction_turn(self):
        body = f"{SCOPE}## Check\n```sh\njq .x f\n```\n"
        answers = [(factory_answer(body, recipe="cleanup"), None), (factory_answer(recipe="cleanup"), None)]
        api = FakeApi([OLD_TURN, NEW_TURN])
        with (
            mock.patch.object(read, "anthropic", api),
            mock.patch.object(read, "wait_idle"),
            mock.patch.object(read, "final_answer", side_effect=answers),
        ):
            answer = read.read_answer(SID, read.FACTORY_CLASSES, FACTORY_FILES, read.FACTORY_CONTRACT, 60)
        self.assertEqual(answer["recipe"], "cleanup")
        sent = api.posts[0][1]["events"][0]["content"][0]["text"]
        self.assertIn("`## Check` runs `jq`", sent)
        self.assertIn(read.FACTORY_CONTRACT, sent)

    def test_a_good_first_answer_posts_nothing(self):
        answer, api, _ = self.read_answer([GOOD])
        self.assertEqual(answer["class"], "code")
        self.assertEqual(api.posts, [])


class TurnTests(unittest.TestCase):
    """The correction turn against the real final_answer: the listing after the POST can lag the POST."""

    def run_correction(self, listings, status="idle", posted=None):
        api = FakeApi(listings, status, posted)
        with (
            mock.patch.object(read, "anthropic", api),
            mock.patch.object(read, "wait_idle"),
            mock.patch.object(read.time, "sleep") as sleep,
        ):
            try:
                return read.read_answer(SID, read.CLASSES, FILES, read.CONTRACT, 60), api, sleep, None
            except SystemExit as exit:
                return None, api, sleep, str(exit.code)

    def test_the_old_idle_does_not_end_the_correction_turn(self):
        old = [OLD_TURN[0], message(STRAY), OLD_TURN[1]]
        posted = [{"type": "user.message", "id": "m2"}] + old
        done = [{"type": "session.status_idle", "stop_details": {"type": "end_turn"}}, message(GOOD)] + posted
        answer, api, sleep, error = self.run_correction([old, old, posted, done])
        self.assertIsNone(error)
        self.assertEqual(len(api.posts), 1)
        self.assertEqual(api.reads, 4)
        self.assertEqual(sleep.call_count, 2)
        self.assertEqual(answer["title"], "t")
        self.assertEqual(answer["evidence"], ["e"])

    def test_a_terminal_status_after_the_post_exits_with_the_status(self):
        old = [OLD_TURN[0], message(STRAY), OLD_TURN[1]]
        posted = [{"type": "user.message", "id": "m2"}] + old
        answer, api, sleep, error = self.run_correction([old, posted], posted="terminated")
        self.assertIsNone(answer)
        self.assertEqual(len(api.posts), 1)
        self.assertEqual(sleep.call_count, 0)
        self.assertIn("terminated", error)
        self.assertTrue(error.startswith(f"repair: session {SID} ended"), error)

    def test_a_terminal_status_after_the_first_turn_exits_before_the_post(self):
        old = [OLD_TURN[0], message(STRAY), OLD_TURN[1]]
        for status in ("terminated", "error"):
            with self.subTest(status=status):
                answer, api, sleep, error = self.run_correction([old], status=status)
                self.assertIsNone(answer)
                self.assertEqual(api.posts, [])
                self.assertEqual(sleep.call_count, 0)
                self.assertTrue(error.startswith(f"repair: session {SID} ended with the status {status} "), error)
                self.assertIn(DELIMITER, error)


class PageTests(unittest.TestCase):
    """turn_events follows `next_page` as ReadBackAsync of src/Core/ManagedAgentsAgent.cs does."""

    def test_a_turn_longer_than_one_page_reads_the_next_page(self):
        first = [{"type": "session.status_idle", "id": "e4"}, {"type": "agent.message", "id": "e3"}]
        second = [{"type": "agent.tool_use", "id": "e2"}, {"type": "user.message", "id": "m1"}, {"id": "e0"}]
        api = FakeApi([{"data": first, "next_page": "p/2+="}, {"data": second, "next_page": "p3"}])
        with mock.patch.object(read, "anthropic", api):
            before, events = read.turn_events(SID)
        self.assertEqual(
            api.paths,
            [
                f"/v1/sessions/{SID}/events?order=desc&limit=100",
                f"/v1/sessions/{SID}/events?order=desc&limit=100&page=p%2F2%2B%3D",
            ],
        )
        self.assertEqual(before, "m1")
        self.assertEqual([e["id"] for e in events], ["e4", "e3", "e2"])

    def test_the_pages_end_before_a_user_message(self):
        api = FakeApi([{"data": [{"id": "e1"}], "next_page": None}])
        with mock.patch.object(read, "anthropic", api):
            before, events = read.turn_events(SID)
        self.assertEqual(api.reads, 1)
        self.assertIsNone(before)
        self.assertEqual(events, [{"id": "e1"}])


if __name__ == "__main__":
    unittest.main()
