"""Unit tests of judge.py: the parse of a reviewer's Report, the read of each angle, for #518, and the dedupe of
already_judged. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/judge -p 'test_*.py'`. No test calls the API: a session is the FakeApi of
.github/shared/fake_anthropic.py, and GitHub is a FakeGitHub. The read of a session's last events moved to
.github/shared/test_sessions.py."""

import time
import unittest
from unittest import mock

import judge
from fake_anthropic import FakeApi
from fake_github import FakeGitHub
from sessions import Session

ACCEPT = '{"angle": "x", "verdict": "accept", "findings": [], "clean": ["the diff"]}'


def message(text):
    return {"type": "agent.message", "content": [{"type": "text", "text": text}]}


def idle(stop="end_turn"):
    return {"type": "session.status_idle", "stop_details": {"type": stop}}


def usage(amount):
    return {"type": "session.usage", "usage": {"list_cost": {"amount": amount}}}


def turn(*events):
    """The listing of one turn, newest first, that ends at the session's first user.message."""
    return list(events) + [{"type": "user.message", "id": "m1"}]


class ParseReportTests(unittest.TestCase):
    def test_a_report_with_a_verdict_parses_with_the_angle_of_the_session(self):
        report = judge.parse_report("Conformance", 'Done.\n{"angle": "other", "verdict": "reject"}')
        self.assertEqual(report, {"angle": "Conformance", "verdict": "reject", "findings": [], "clean": []})

    def test_a_fault_raises_value_error(self):
        for text, fault in (
            ("no object", "JSONDecodeError: "),
            ('{"verdict": "maybe"}', "the verdict 'maybe' is not one of accept, reject"),
            ("[1]", "the Report is a JSON list, not an object"),
        ):
            with self.subTest(text=text):
                with self.assertRaises(ValueError) as raised:
                    judge.parse_report("Conformance", text)
                self.assertIn(fault, str(raised.exception))

    def test_the_shape_is_the_one_in_the_prompt(self):
        shape = judge.REPORT.format(angle="Conformance")
        self.assertIn('{"angle": "Conformance", "verdict": "accept" or "reject",', shape)
        self.assertIn(shape, judge.reviewer_prompt("Conformance", "o/r", "h", "b", "the spec"))


class ReadAnglesTests(unittest.TestCase):
    def sessions(self, **listings):
        """One session for each angle, over the listings of its turns, oldest turn first."""
        return {
            angle: Session(
                FakeApi(turns if isinstance(turns, tuple) else [turns], sid=f"sesn_{angle}"), f"sesn_{angle}"
            )
            for angle, turns in listings.items()
        }

    def read(self, started):
        with mock.patch("sessions.time.sleep"), mock.patch("builtins.print"):
            return judge.read_angles(started, time.time() + 60)

    def test_a_session_error_on_one_angle_rejects_that_angle_and_the_others_are_read(self):
        started = self.sessions(
            Conformance=turn(idle("refusal"), message("I will not")),
            Regressions=turn(idle(), message(ACCEPT), usage("12")),
        )
        reports, costs = self.read(started)
        refused, accepted = reports
        self.assertEqual(refused["angle"], "Conformance")
        self.assertEqual(refused["verdict"], "reject")
        [finding] = refused["findings"]
        self.assertEqual(finding["fault"], "refused")
        self.assertEqual(finding["evidence"], "I will not")
        self.assertEqual(accepted["angle"], "Regressions")
        self.assertEqual(accepted["verdict"], "accept")
        self.assertEqual(costs, [("sesn_Conformance", "Conformance", "?"), ("sesn_Regressions", "Regressions", "12")])

    def test_a_second_bad_message_rejects_with_the_faults_of_both(self):
        first = turn(idle(), message("not json"))
        second = [idle(), message('{"verdict": "maybe"}'), {"type": "user.message", "id": "m2"}] + first
        started = self.sessions(Conformance=(first, second))
        [report], _ = self.read(started)
        fault = report["findings"][0]["fault"]
        self.assertTrue(fault.startswith("the parser says: first JSONDecodeError"), fault)
        self.assertIn("then the verdict 'maybe'", fault)
        self.assertEqual(len(started["Conformance"].api.posts), 1)

    def test_a_deadline_that_passes_rejects_the_angle(self):
        started = self.sessions(Conformance=turn(idle(), message(ACCEPT)))
        started["Conformance"].api.status = "running"
        with mock.patch("builtins.print"):
            [report], _ = judge.read_angles(started, time.time() - 1)
        self.assertRegex(report["findings"][0]["fault"], r"^not idle after \d+ s$")


class AlreadyJudgedTests(unittest.TestCase):
    """The marker of the head SHA and the spec digest, read from every comment of the pull request through api_list."""

    SHA = "a" * 40

    def judged(self, *bodies, spec="the spec"):
        gh = FakeGitHub()
        gh.answer_list("repos/o/r/issues/7/comments", [{"body": body} for body in bodies])
        return judge.already_judged(gh, "o/r", 7, self.SHA, spec), gh

    def marker(self, spec="the spec"):
        return judge.MARKER.format(sha=self.SHA, spec=judge.spec_digest(spec))

    def test_a_comment_with_the_marker_of_the_head_and_the_spec_is_judged(self):
        judged, gh = self.judged("other", f"{self.marker()}\n**APPROVED**", None)
        self.assertTrue(judged)
        self.assertEqual(gh.calls, [(("api_list", "repos/o/r/issues/7/comments"), None)])

    def test_the_same_head_with_another_spec_is_not_judged(self):
        self.assertFalse(self.judged(self.marker("the old spec"))[0])

    def test_no_marker_is_not_judged(self):
        self.assertFalse(self.judged("a comment", judge.MARKER.format(sha="b" * 40, spec=judge.spec_digest("x")))[0])


if __name__ == "__main__":
    unittest.main()
