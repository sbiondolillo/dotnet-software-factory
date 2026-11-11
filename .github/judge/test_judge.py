"""Unit tests of judge.py: the parse of a reviewer's Report, the read of each angle, for #518, the dedupe of
already_judged, the outcome of render, the verdict of main, for #557, and the spec of spec_for. The build job of
ci.yml runs them with `python3 -B -m unittest discover -s .github/judge -p 'test_*.py'`. No test calls the API: a
session is the FakeApi of .github/shared/fake_anthropic.py, and GitHub is a FakeGitHub. The read of a session's last
events moved to .github/shared/test_sessions.py."""

import time
import unittest
from unittest import mock

import closing
import judge
from fake_anthropic import FakeApi, FakeSessions
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

    def test_a_session_error_on_one_angle_gives_no_report_of_that_angle_and_the_others_are_read(self):
        started = self.sessions(
            Conformance=turn(idle("refusal"), message("I will not")),
            Regressions=turn(idle(), message(ACCEPT), usage("12")),
        )
        results, costs = self.read(started)
        refused, accepted = results
        self.assertEqual(refused, judge.Missing("Conformance", "refused", "I will not"))
        self.assertEqual(accepted["angle"], "Regressions")
        self.assertEqual(accepted["verdict"], "accept")
        self.assertEqual(costs, [("sesn_Conformance", "Conformance", "?"), ("sesn_Regressions", "Regressions", "12")])

    def test_a_second_bad_message_gives_no_report_with_the_faults_of_both(self):
        first = turn(idle(), message("not json"))
        second = [idle(), message('{"verdict": "maybe"}'), {"type": "user.message", "id": "m2"}] + first
        started = self.sessions(Conformance=(first, second))
        [missing], _ = self.read(started)
        self.assertTrue(missing.reason.startswith("the parser says: first JSONDecodeError"), missing.reason)
        self.assertIn("then the verdict 'maybe'", missing.reason)
        self.assertEqual(len(started["Conformance"].api.posts), 1)

    def test_a_deadline_that_passes_gives_no_report(self):
        started = self.sessions(Conformance=turn(idle(), message(ACCEPT)))
        started["Conformance"].api.status = "running"
        with mock.patch("builtins.print"):
            [missing], _ = judge.read_angles(started, time.time() - 1)
        self.assertRegex(missing.reason, r"^not idle after \d+ s$")


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


class RenderTests(unittest.TestCase):
    """The outcome of the reports, which main() posts as the title of judge-verdict, and the head of the review."""

    PR = {"headRefOid": "a" * 40}

    def report(self, verdict):
        return {"angle": "Conformance", "verdict": verdict, "findings": [], "clean": []}

    def test_every_angle_that_accepts_is_approved(self):
        outcome, body = judge.render([self.report("accept")], None, self.PR, [], "the spec")
        self.assertEqual(outcome, "APPROVED")
        self.assertIn("**APPROVED** by the judge on `aaaaaaa`.", body)

    def test_one_angle_that_rejects_requests_changes(self):
        outcome, body = judge.render([self.report("accept"), self.report("reject")], None, self.PR, [], "the spec")
        self.assertEqual(outcome, "CHANGES REQUESTED")
        self.assertIn("**CHANGES REQUESTED** by the judge", body)

    def test_an_angle_with_no_report_keeps_its_section_with_the_reason_and_the_last_message(self):
        results = [
            judge.Missing("Regressions and style", "refused", "I will not\n"),
            self.report("reject"),
            judge.Missing("Test strength, by mutation", "not idle after 1800 s", ""),
        ]
        outcome, body = judge.render(results, None, self.PR, [], "the spec")
        self.assertEqual(outcome, "CHANGES REQUESTED")
        self.assertIn(
            "### Regressions and style: no Report\n- refused\n  ```\n  I will not\n  ```\n\n### Conformance: reject\n",
            body,
        )
        self.assertIn("### Test strength, by mutation: no Report\n- not idle after 1800 s\n\n", body)

    def test_a_last_message_of_several_lines_stays_in_its_code_block(self):
        text = "I will not.\n```\nThe change is out of scope.\n````"
        outcome, body = judge.render([judge.Missing("Regressions", "refused", text)], None, self.PR, [], "the spec")
        self.assertIn(
            "### Regressions: no Report\n- refused\n  `````\n  I will not.\n  ```\n  The change is out of scope.\n"
            "  ````\n  `````\n\n",
            body,
        )

    def test_the_evidence_of_a_finding_of_several_lines_stays_in_its_code_block(self):
        finding = {"file": "a.py", "line": 3, "fault": "the fault", "evidence": "$ cat a.md\n```\nprint(1)\n```"}
        accept = {"angle": "Regressions", "verdict": "accept", "findings": [], "clean": []}
        results = [{**self.report("reject"), "findings": [finding]}, accept]
        outcome, body = judge.render(results, None, self.PR, [], "the spec")
        self.assertIn(
            "- `a.py:3` the fault\n  ````\n  $ cat a.md\n  ```\n  print(1)\n  ```\n  ````\n\n### Regressions: accept\n",
            body,
        )


REJECT = (
    '{"verdict": "reject", "findings": [{"file": "a.py", "line": 3, "fault": "the fault", "evidence": "the output", '
    '"fix": "the fix"}], "clean": []}'
)
HEAD = "a" * 40
ANGLES = "Conformance,Regressions and style"
OVERLOADED = "the API failed: GET /v1/sessions/sesn_r/events?order=desc&limit=100 -> 500: overloaded"


def check_run(conclusion, title, summary):
    """The args of the gh call that posts judge-verdict."""
    fields = {
        "name": "judge-verdict",
        "head_sha": HEAD,
        "status": "completed",
        "conclusion": conclusion,
        "output[title]": title,
        "output[summary]": summary,
    }
    return ("api", "repos/o/r/check-runs", *[arg for key, value in fields.items() for arg in ("-f", f"{key}={value}")])


class MainTests(unittest.TestCase):
    """For #557, the verdict of main() over a FakeSessions of the angles, in the order of ANGLES, and the synthesis.
    Only a Report decides: an angle with no Report never rejects and never accepts. JUDGE_TIMEOUT_SECONDS is 0, so an
    angle that is idle gives its Report and an angle that is running passes the deadline at once."""

    COMMENT = ("pr", "comment", "7", "-R", "o/r", "--body-file", "-")
    CAUSES = {
        "a deadline": (lambda: FakeApi([turn()], status="running", sid="sesn_r"), "not idle after 0 s"),
        "an API error in the read": (lambda: FakeApi([turn()], failing=("events",), sid="sesn_r"), OVERLOADED),
    }

    def angle(self, text, sid="sesn_c"):
        return FakeApi([turn(idle(), message(text), usage("4"))], sid=sid)

    def synthesis(self, failing=()):
        return FakeApi([turn(idle(), message("The synthesis."), usage("1"))], sid="sesn_s", failing=failing)

    def run_main(self, *apis, posts=()):
        """main() for pull request 7, with an answer for each gh call in `posts`. A gh call with no answer fails the
        test. The result is the FakeGitHub, the FakeSessions, the exit code or None, and the printed lines."""
        gh = FakeGitHub()
        fields = f"number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url,{closing.FIELD}"
        pr = {"number": 7, "headRefOid": HEAD, "baseRefOid": "b" * 40, "body": "", "title": "The change"}
        gh.answer(("pr", "view", "7", "-R", "o/r", "--json", fields), {**pr, **closes("o/r")})
        gh.answer_list("repos/o/r/issues/7/comments", [])
        for args in posts:
            gh.answer(args, "{}")
        api = FakeSessions(*apis)
        environment = {
            "GITHUB_REPOSITORY": "o/r",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "JUDGE_AGENT_ID": "agt",
            "JUDGE_ENVIRONMENT_ID": "env",
            "JUDGE_GITHUB_RO_TOKEN": "ro",
            "JUDGE_ANGLES": ANGLES,
            "JUDGE_TIMEOUT_SECONDS": "0",
        }
        code = None
        with (
            mock.patch.dict("os.environ", environment),
            mock.patch("sys.argv", ["judge.py", "7"]),
            mock.patch("sessions.time.sleep"),
            mock.patch("builtins.print") as printed,
        ):
            try:
                judge.main(gh, api)
            except SystemExit as stop:
                code = stop.code
        return gh, api, code, [" ".join(map(str, call.args)) for call in printed.call_args_list]

    def comment(self, gh):
        [body] = [text for args, text in gh.calls if args == self.COMMENT]
        return body

    def test_a_reject_and_an_angle_with_no_report_post_the_reject_with_a_no_report_section(self):
        for cause, (missing, reason) in self.CAUSES.items():
            with self.subTest(cause=cause):
                verdict = check_run("failure", "CHANGES REQUESTED", "1 of 2 angles, 1 findings")
                gh, api, code, _ = self.run_main(
                    self.angle(REJECT), missing(), self.synthesis(), posts=(self.COMMENT, verdict)
                )
                self.assertIsNone(code)
                body = self.comment(gh)
                self.assertIn("### Conformance: reject\n- `a.py:3` the fault\n", body)
                self.assertIn(f"\n### Regressions and style: no Report\n- {reason}\n\n", body)
                self.assertLess(body.index("### Conformance"), body.index("### Regressions and style"))
                self.assertIn("The synthesis.", body)
                self.assertIn(verdict, gh.args())
                prompt = api.started["sesn_s"].posts[0][1]["initial_events"][0]["content"][0]["text"]
                self.assertIn('"angle": "Conformance"', prompt)
                self.assertNotIn("Regressions and style", prompt)

    def test_an_accept_and_an_angle_with_no_report_post_nothing_and_exit_with_the_code_1(self):
        for cause, (missing, reason) in self.CAUSES.items():
            with self.subTest(cause=cause):
                gh, api, code, printed = self.run_main(self.angle(ACCEPT), missing())
                self.assertEqual(code, 1)
                self.assertNotIn(self.COMMENT, gh.args())
                self.assertFalse([args for args in gh.args() if "repos/o/r/check-runs" in args])
                self.assertIn(f"judge: no Report from Regressions and style: {reason}", printed)
                self.assertNotIn("sesn_s", api.started)

    def test_every_angle_that_accepts_approves(self):
        verdict = check_run("success", "APPROVED", "2 of 2 angles, 0 findings")
        gh, _, code, _ = self.run_main(self.angle(ACCEPT), self.angle(ACCEPT, "sesn_r"), posts=(self.COMMENT, verdict))
        self.assertIsNone(code)
        self.assertIn("**APPROVED**", self.comment(gh))
        self.assertIn(verdict, gh.args())

    def test_a_synthesis_that_fails_to_start_still_posts_the_reject_with_no_synthesis_text(self):
        verdict = check_run("failure", "CHANGES REQUESTED", "2 of 2 angles, 1 findings")
        gh, _, code, printed = self.run_main(
            self.angle(REJECT),
            self.angle(ACCEPT, "sesn_r"),
            self.synthesis(failing=("start",)),
            posts=(self.COMMENT, verdict),
        )
        self.assertIsNone(code)
        lines = self.comment(gh).splitlines()
        self.assertEqual(
            lines[1:4], ["**CHANGES REQUESTED** by the judge on `aaaaaaa`.", "", "### Conformance: reject"]
        )
        self.assertNotIn("synthesis", lines[-1])
        self.assertIn(verdict, gh.args())
        self.assertIn("judge: the synthesis did not start: POST /v1/sessions -> 500: overloaded", printed)


class SpecForTests(unittest.TestCase):
    """The spec is the issue that GitHub reports the pull request closes, else the issue of a line `Part of #<n>`
    outside code, else the pull request body."""

    def spec(self, body, *numbers):
        gh = FakeGitHub()
        for number in (5, 6):
            issue = {"number": number, "title": f"Issue {number}", "body": f"The spec of #{number}."}
            gh.answer(("issue", "view", str(number), "-R", "o/r", "--json", "number,title,body"), issue)
        pr = {"number": 7, "title": "The change", "body": body, **closes("o/r", *numbers)}
        return judge.spec_for(gh, "o/r", pr)

    def test_the_issue_github_reports_is_the_spec(self):
        self.assertEqual(self.spec("No keyword here.", 5), ("# Issue 5 (#5)\n\nThe spec of #5.", 5))

    def test_the_closing_issue_wins_over_a_part_of_line(self):
        self.assertEqual(self.spec("Part of #6", 5)[1], 5)

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
