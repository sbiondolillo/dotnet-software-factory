"""Unit tests of the read of a review session's last events in judge.py, for #470. The build job of ci.yml runs them
with `python3 -B -m unittest discover -s .github/judge -p 'test_*.py'`. No test calls the API: each one replaces
judge.anthropic with a fake listing of the events of one session."""

import unittest
import urllib.parse
from unittest import mock

import judge

SID = "sesn_1"


class FakeEvents:
    """The events listing of one session, oldest first in `events`. A GET honors `order`, `limit` and `page` as the
    API does: a page holds `limit` events, and `next_page` names the next page, or is None on the last one."""

    def __init__(self, events):
        self.events = events
        self.paths = []

    def __call__(self, method, path, body=None):
        assert method == "GET", method
        self.paths.append(path)
        url = urllib.parse.urlparse(path)
        assert url.path == f"/v1/sessions/{SID}/events", url.path
        query = urllib.parse.parse_qs(url.query)
        events = self.events[::-1] if query.get("order") == ["desc"] else self.events
        limit = int(query.get("limit", ["20"])[0])
        start = int(query.get("page", ["0"])[0])
        end = start + limit
        return {"data": events[start:end], "next_page": str(end) if end < len(events) else None}


def message(text):
    return {"type": "agent.message", "content": [{"type": "text", "text": text}]}


def idle(stop):
    return {"type": "session.status_idle", "stop_details": {"type": stop}}


def usage(amount):
    return {"type": "session.usage", "usage": {"list_cost": {"amount": amount}}}


def tools(count):
    return [{"type": "agent.tool_use"} for _ in range(count)]


class FinalMessageTests(unittest.TestCase):
    def read(self, events):
        api = FakeEvents(events)
        with mock.patch.object(judge, "anthropic", api):
            return judge.final_message(SID), api

    def test_the_final_message_after_event_200_is_the_one_read(self):
        events = [message("old"), idle("end_turn"), usage("1")] + tools(240) + [message("new"), idle("refusal")]
        events += [usage("9")]
        (text, stop, spent), _ = self.read(events)
        self.assertEqual(text, "new")
        self.assertEqual(stop, {"type": "refusal"})
        self.assertEqual(spent, {"list_cost": {"amount": "9"}})

    def test_the_read_follows_next_page_to_the_newest_usage(self):
        events = [usage("4")] + tools(150) + [message("report"), idle("end_turn")]
        (text, stop, spent), api = self.read(events)
        self.assertEqual((text, stop, spent), ("report", {"type": "end_turn"}, {"list_cost": {"amount": "4"}}))
        self.assertEqual(len(api.paths), 2)
        self.assertIn("order=desc", api.paths[0])
        self.assertIn("page=100", api.paths[1])

    def test_the_read_stops_once_it_has_all_three(self):
        events = tools(300) + [message("report"), usage("2"), idle("end_turn")]
        _, api = self.read(events)
        self.assertEqual(len(api.paths), 1)

    def test_the_last_text_of_the_final_message_is_the_report(self):
        final = {"type": "agent.message", "content": [{"type": "text", "text": "a"}, {"type": "text", "text": "b"}]}
        (text, _, _), _ = self.read([final, idle("end_turn")])
        self.assertEqual(text, "b")

    def test_an_empty_session_reads_nothing(self):
        (result, api) = self.read([])
        self.assertEqual(result, ("", None, {}))
        self.assertEqual(len(api.paths), 1)


if __name__ == "__main__":
    unittest.main()
