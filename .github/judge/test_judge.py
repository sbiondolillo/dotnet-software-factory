"""Unit tests of judge.py: the parse of a reviewer's Report, the read of each angle, for #518, and the dedupe of
already_judged, and the spec of spec_for. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/judge -p 'test_*.py'`. No test calls the API: a session is the FakeApi of
.github/shared/fake_anthropic.py, and GitHub is a FakeGitHub. The read of a session's last events moved to
.github/shared/test_sessions.py."""

import time
import unittest
from unittest import mock

import closing
import judge
from fake_anthropic import FakeApi
from fake_github import FakeGitHub, closes
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


class ReadSynthesisTests(unittest.TestCase):
    def read(self, listing, status="idle", deadline=60):
        session = Session(FakeApi([listing], status=status, sid="sesn_s"), "sesn_s")
        with mock.patch("builtins.print"):
            return judge.read_synthesis(session, time.time() + deadline)

    def test_the_text_is_kept_as_it_is(self):
        self.assertEqual(self.read(turn(idle(), message("### Review {x}"), usage("3"))), ("### Review {x}", "3"))

    def test_a_refused_synthesis_gives_its_text_and_the_review_is_still_posted(self):
        self.assertEqual(self.read(turn(idle("refusal"), message("the text"))), ("the text", "?"))

    def test_an_empty_synthesis_gives_no_text(self):
        self.assertEqual(self.read(turn(idle(), message(""), usage("1"))), (None, "1"))

    def test_a_synthesis_past_the_deadline_gives_no_text(self):
        self.assertEqual(self.read(turn(), status="running", deadline=-1), (None, "?"))


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


class SpecForTests(unittest.TestCase):
    """The spec is the issue that GitHub reports the pull request closes, else the issue of a line `Part of #<n>`
    outside code, else the pull request body."""

    def spec(self, body, references=()):
        gh = FakeGitHub()
        for number in (5, 6):
            issue = {"number": number, "title": f"Issue {number}", "body": f"The spec of #{number}."}
            gh.answer(("issue", "view", str(number), "-R", "o/r", "--json", "number,title,body"), issue)
        pr = {"number": 7, "title": "The change", "body": body, closing.FIELD: list(references)}
        return judge.spec_for(gh, "o/r", pr)

    def test_the_issue_github_reports_is_the_spec(self):
        self.assertEqual(self.spec("No keyword here.", closes("o/r", 5)), ("# Issue 5 (#5)\n\nThe spec of #5.", 5))

    def test_the_closing_issue_wins_over_a_part_of_line(self):
        self.assertEqual(self.spec("Part of #6", closes("o/r", 5))[1], 5)

    def test_a_closing_keyword_that_github_does_not_report_names_no_issue(self):
        self.assertEqual(self.spec("Fixes #5")[1], None)

    def test_a_part_of_line_names_the_issue_when_github_reports_none(self):
        self.assertEqual(self.spec("The change.\n\nPart of #6\n"), ("# Issue 6 (#6)\n\nThe spec of #6.", 6))

    def test_a_part_of_line_in_code_names_no_issue(self):
        for body in ("`Part of #6`", "```\nPart of #6\n```"):
            with self.subTest(body=body):
                self.assertEqual(self.spec(body), (f"# The change (pull request #7)\n\n{body}", None))

    def test_a_body_that_names_no_issue_is_the_spec(self):
        self.assertEqual(self.spec(None), ("# The change (pull request #7)\n\n", None))


if __name__ == "__main__":
    unittest.main()
