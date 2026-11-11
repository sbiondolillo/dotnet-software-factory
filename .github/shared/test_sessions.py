"""Unit tests of sessions.py, for #518: the read of a session's last turn, its pages, the Report and its one correction
turn. The build job of ci.yml runs them with `python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. The
FakeApi of fake_anthropic.py stands in for the adapter, so no test calls the API, and time.sleep is replaced where the
wait polls."""

import json
import time
import unittest
from unittest import mock

import sessions
from anthropic import AnthropicError
from fake_anthropic import SID, FakeApi, paged
from sessions import Session, SessionError

GOOD = '{"class": "code", "title": "t"}'
# The slip of run 41338664688: a stray `]` closed the body string.
STRAY = '{"class": "code", "title": "t"], "body": ""}'
DELIMITER = "Expecting ',' delimiter"
SHAPE = 'Your final answer is one JSON object: {"class": ..., "title": ...}'

OLD_TURN = [
    {"type": "session.status_idle", "stop_details": {"type": "end_turn"}},
    {"type": "user.message", "id": "m1"},
]


def message(text):
    return {"type": "agent.message", "content": [{"type": "text", "text": text}]}


def idle(stop="end_turn"):
    return {"type": "session.status_idle", "stop_details": {"type": stop}}


def usage(amount):
    return {"type": "session.usage", "usage": {"list_cost": {"amount": amount}}}


def tools(count):
    return [{"type": "agent.tool_use"} for _ in range(count)]


def parse_json(text):
    """A parse function: the object with the keys `class` and `title`, or ValueError with the fault."""
    try:
        report = sessions.json_object(text)
        return {"class": report["class"], "title": report["title"]}
    except KeyError as error:
        raise ValueError(f"KeyError: {error}") from error
    except ValueError as error:
        raise ValueError(f"{type(error).__name__}: {error}") from error


def as_text(text):
    return text


class Read:
    """A report of one session over a FakeApi, with time.sleep replaced. `result` is the Answer, or `error` the
    SessionError."""

    def __init__(self, listings, status="idle", posted=None, parse=parse_json, deadline=None, failing=()):
        self.api = FakeApi(listings, status, posted, failing=failing)
        session = Session(self.api, SID)
        self.result = self.error = None
        with mock.patch.object(sessions.time, "sleep") as self.sleep:
            try:
                self.result = session.report(parse, SHAPE, deadline or time.time() + 60)
            except SessionError as error:
                self.error = error


class StartTests(unittest.TestCase):
    def test_start_posts_the_session_and_keeps_its_id(self):
        api = FakeApi()
        resources = [sessions.repository("o/r", "ro-token")]
        tools_off = {"tools": [{"type": "agent_toolset_20260401", "default_config": {"enabled": False}}]}
        session = Session.start(api, "agt", "env", "a title", "the prompt", {"k": "v"}, 150, resources, tools_off)
        self.assertEqual(session.sid, SID)
        [(path, body)] = api.posts
        self.assertEqual(path, "/v1/sessions")
        self.assertEqual(body["agent"], {"type": "agent_with_overrides", "id": "agt", **tools_off})
        self.assertEqual(body["environment_id"], "env")
        self.assertEqual(body["budget"], {"type": "limit", "max_list_cost": {"amount": "150", "currency": "USD"}})
        self.assertEqual(body["initial_events"][0]["content"], [{"type": "text", "text": "the prompt"}])
        self.assertEqual(
            body["resources"],
            [
                {
                    "type": "github_repository",
                    "url": "https://github.com/o/r",
                    "mount_path": "/workspace/r",
                    "authorization_token": "ro-token",
                }
            ],
        )
        json.dumps(body)

    def test_a_start_with_no_resources_and_no_overrides_names_the_agent_alone(self):
        api = FakeApi()
        Session.start(api, "agt", "env", "t", "p", {}, 100)
        body = api.posts[0][1]
        self.assertEqual(body["agent"], "agt")
        self.assertNotIn("resources", body)


class FinalMessageTests(unittest.TestCase):
    """The read of the last turn, moved from the final_message tests of the judge, for #470."""

    def test_the_final_message_after_event_200_is_the_one_read(self):
        events = [message("old"), idle(), usage("1")] + tools(240) + [message("new"), idle(), usage("9")]
        read = Read(paged(events[::-1]), parse=as_text)
        self.assertEqual(read.result.value, "new")
        self.assertEqual(read.result.usage, {"list_cost": {"amount": "9"}})

    def test_the_read_follows_next_page_to_the_newest_usage(self):
        events = [{"type": "user.message", "id": "m1"}, usage("4")] + tools(150) + [message(GOOD), idle()]
        read = Read(paged(events[::-1]))
        self.assertEqual(read.result.value, {"class": "code", "title": "t"})
        self.assertEqual(read.result.usage, {"list_cost": {"amount": "4"}})
        self.assertEqual(len(read.api.paths), 2)
        self.assertIn("order=desc", read.api.paths[0])
        self.assertIn("page=100", read.api.paths[1])

    def test_the_read_stops_at_the_user_message_of_the_turn(self):
        events = tools(300) + [{"type": "user.message", "id": "m1"}, message(GOOD), usage("2"), idle()]
        read = Read(paged(events[::-1]))
        self.assertEqual(read.api.reads, 1)
        self.assertEqual(read.result.usage, {"list_cost": {"amount": "2"}})

    def test_the_last_text_of_the_final_message_is_the_report(self):
        final = {"type": "agent.message", "content": [{"type": "text", "text": "a"}, {"type": "text", "text": "b"}]}
        read = Read([[idle(), final]], parse=as_text)
        self.assertEqual(read.result.value, "b")

    def test_an_empty_session_reads_nothing(self):
        read = Read([[]], parse=as_text)
        self.assertEqual((read.result.value, read.result.usage, read.result.corrected), ("", {}, False))
        self.assertEqual(read.api.reads, 1)

    def test_a_good_first_message_is_not_corrected_and_has_the_last_usage(self):
        read = Read([[idle(), usage("7"), message(GOOD), usage("3"), {"type": "user.message", "id": "m1"}]])
        self.assertFalse(read.result.corrected)
        self.assertEqual(read.result.usage, {"list_cost": {"amount": "7"}})
        self.assertEqual(read.api.posts, [])


class StopTests(unittest.TestCase):
    def test_a_refusal_raises_with_the_reason_refused(self):
        read = Read([[idle("refusal"), message("no"), {"type": "user.message", "id": "m1"}]])
        self.assertEqual((read.error.sid, read.error.reason, read.error.text), (SID, "refused", "no"))
        self.assertEqual(read.api.posts, [])

    def test_a_refusal_named_by_stop_reason_raises_too(self):
        refused = {"type": "session.status_idle", "stop_reason": {"type": "refusal"}}
        read = Read([[refused, message(GOOD), {"type": "user.message", "id": "m1"}]])
        self.assertEqual(read.error.reason, "refused")

    def test_no_wait_sleeps_past_the_deadline(self):
        api = FakeApi([[message(GOOD)]], status="running")
        with mock.patch.object(sessions.time, "sleep") as sleep:
            with self.assertRaises(SessionError):
                Session(api, SID).report(parse_json, SHAPE, time.time() + 0.05, poll=15)
        self.assertTrue(sleep.call_args_list)
        self.assertTrue(all(call.args[0] <= 0.05 for call in sleep.call_args_list), sleep.call_args_list)

    def test_a_deadline_that_passes_raises_with_the_time_in_the_reason(self):
        read = Read([[]], status="running", deadline=time.time() - 1)
        self.assertRegex(read.error.reason, r"^not idle after \d+ s$")
        self.assertEqual(read.api.reads, 0)

    def test_the_wait_polls_until_the_session_is_idle(self):
        api = FakeApi([[message(GOOD)]], status="running")
        statuses = iter(["running", "running", "idle"])
        api.get = mock.Mock(
            side_effect=lambda path, get=api.get: {"status": next(statuses)} if path.endswith(SID) else get(path)
        )
        with mock.patch.object(sessions.time, "sleep") as sleep:
            answer = Session(api, SID).report(parse_json, SHAPE, time.time() + 60, poll=7)
        self.assertEqual(answer.value["class"], "code")
        self.assertEqual([call.args for call in sleep.call_args_list], [(7,), (7,)])


class ApiErrorTests(unittest.TestCase):
    """For #557: an AnthropicError in the read becomes a SessionError with the session id, so the caller handles one
    error, and the reason holds the text of the AnthropicError."""

    def test_an_api_error_in_each_read_raises_session_error(self):
        first = [idle(), message(STRAY), {"type": "user.message", "id": "m1"}]
        for kind, path in (
            ("status", f"GET /v1/sessions/{SID} -> 500: overloaded"),
            ("events", f"GET /v1/sessions/{SID}/events?order=desc&limit=100 -> 500: overloaded"),
            ("message", f"POST /v1/sessions/{SID}/events -> 500: overloaded"),
        ):
            with self.subTest(kind=kind), mock.patch("builtins.print"):
                read = Read([first], failing=(kind,))
                self.assertIsNone(read.result)
                self.assertEqual(read.error.sid, SID)
                self.assertEqual(read.error.reason, f"the API failed: {path}")
                self.assertIsInstance(read.error.__cause__, AnthropicError)


class CorrectionTests(unittest.TestCase):
    """The one correction turn, moved from the reader's tests, for #407."""

    def corrected(self, first, second):
        old = [OLD_TURN[0], message(first), OLD_TURN[1]]
        new = [idle(), message(second), {"type": "user.message", "id": "m2"}] + old
        return Read([old, new])

    def test_a_bad_message_gets_one_correction_turn_in_the_same_session(self):
        read = self.corrected(STRAY, GOOD)
        self.assertEqual(read.result.value, {"class": "code", "title": "t"})
        self.assertTrue(read.result.corrected)
        [(path, body)] = read.api.posts
        self.assertEqual(path, f"/v1/sessions/{SID}/events")
        self.assertEqual(len(body["events"]), 1)
        event = body["events"][0]
        self.assertEqual(event["type"], "user.message")
        self.assertEqual([c["type"] for c in event["content"]], ["text"])
        self.assertIn(DELIMITER, event["content"][0]["text"])
        self.assertIn(SHAPE, event["content"][0]["text"])
        json.dumps(body)

    def test_two_bad_messages_raise_with_both_faults(self):
        read = self.corrected(STRAY, '{"class": "code"}')
        self.assertEqual(len(read.api.posts), 1)
        self.assertTrue(read.error.reason.startswith("the parser says: "), read.error.reason)
        self.assertIn(DELIMITER, read.error.reason)
        self.assertIn("KeyError: 'title'", read.error.reason)
        self.assertEqual(read.error.text, '{"class": "code"}')

    def test_a_good_first_message_posts_nothing(self):
        read = self.corrected(GOOD, STRAY)
        self.assertEqual(read.result.value["class"], "code")
        self.assertEqual(read.api.posts, [])


class TurnTests(unittest.TestCase):
    """The correction turn: the listing after the POST can lag the POST."""

    OLD = [OLD_TURN[0], message(STRAY), OLD_TURN[1]]
    POSTED = [{"type": "user.message", "id": "m2"}] + OLD

    def test_the_old_idle_does_not_end_the_correction_turn(self):
        done = [idle(), message(GOOD)] + self.POSTED
        read = Read([self.OLD, self.OLD, self.POSTED, done])
        self.assertIsNone(read.error)
        self.assertEqual(len(read.api.posts), 1)
        self.assertEqual(read.api.reads, 4)
        self.assertEqual(read.sleep.call_count, 2)
        self.assertEqual(read.result.value["title"], "t")

    def test_a_terminal_status_after_the_post_raises_with_the_status(self):
        read = Read([self.OLD, self.POSTED], posted="terminated")
        self.assertIsNone(read.result)
        self.assertEqual(len(read.api.posts), 1)
        self.assertEqual(read.sleep.call_count, 0)
        self.assertTrue(read.error.reason.startswith("ended as terminated"), read.error.reason)

    def test_a_terminal_status_after_the_first_turn_raises_before_the_post(self):
        for status in ("terminated", "error"):
            with self.subTest(status=status):
                read = Read([self.OLD], status=status)
                self.assertIsNone(read.result)
                self.assertEqual(read.api.posts, [])
                self.assertEqual(read.sleep.call_count, 0)
                self.assertTrue(read.error.reason.startswith(f"ended as {status} "), read.error.reason)
                self.assertIn(DELIMITER, read.error.reason)

    def test_a_terminal_session_with_a_good_message_gives_its_report(self):
        read = Read([[idle(), message(GOOD)]], status="terminated")
        self.assertEqual(read.result.value["class"], "code")


class PageTests(unittest.TestCase):
    """The read follows `next_page` as ReadBackAsync of src/Core/ManagedAgentsAgent.cs does."""

    def test_a_turn_longer_than_one_page_reads_the_next_page(self):
        first = [idle(), usage("1"), {"type": "agent.message", "id": "e3", "content": [{"type": "text", "text": GOOD}]}]
        second = [{"type": "agent.tool_use", "id": "e2"}, {"type": "user.message", "id": "m1"}, {"id": "e0"}]
        read = Read([{"data": first, "next_page": "p/2+="}, {"data": second, "next_page": "p3"}])
        self.assertEqual(
            read.api.paths,
            [
                f"/v1/sessions/{SID}/events?order=desc&limit=100",
                f"/v1/sessions/{SID}/events?order=desc&limit=100&page=p%2F2%2B%3D",
            ],
        )
        self.assertEqual(read.result.value["title"], "t")

    def test_the_pages_end_before_a_user_message(self):
        read = Read([{"data": [idle(), message(GOOD)], "next_page": None}])
        self.assertEqual(read.api.reads, 1)
        self.assertEqual(read.result.value["class"], "code")

    def test_a_turn_with_no_usage_takes_the_usage_before_it(self):
        events = [idle(), message(GOOD), {"type": "user.message", "id": "m1"}, usage("5")]
        read = Read([{"data": events[:2], "next_page": "2"}, {"data": events[2:], "next_page": None}])
        self.assertEqual(read.result.usage, {"list_cost": {"amount": "5"}})
        self.assertEqual(read.api.reads, 2)


class JsonObjectTests(unittest.TestCase):
    def test_the_object_inside_prose_and_a_fence_is_found(self):
        self.assertEqual(sessions.json_object('Here:\n```json\n{"a": {"b": 1}}\n```\nDone.'), {"a": {"b": 1}})

    def test_a_text_with_no_object_raises_value_error(self):
        with self.assertRaises(ValueError):
            sessions.json_object("no report")


if __name__ == "__main__":
    unittest.main()
