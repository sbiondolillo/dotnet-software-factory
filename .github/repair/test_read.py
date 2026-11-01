"""Unit tests of the parse of the reader's answer and of its one correction turn in read.py, for #407. The build job
of ci.yml runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. No test calls the API:
each one replaces read.anthropic and read.wait_idle, and time.sleep where the wait polls."""

import json
import unittest
from unittest import mock

import read

SID = "sesn_1"
FILES = ("code", "dependency")
GOOD = '{"class": "code", "title": "t", "body": "b", "evidence": ["e"]}'
# The slip of run 41338664688: a stray `]` closed the body string.
STRAY = '{"class": "code", "title": "t", "body": "b"], "evidence": []}'
DELIMITER = "Expecting ',' delimiter"


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
