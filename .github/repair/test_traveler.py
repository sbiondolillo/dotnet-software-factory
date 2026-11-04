"""Unit tests of traveler.py, the one writer and reader of the block under `## Run` of a repair issue, for #513. The
build job of ci.yml runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`."""

import dataclasses
import unittest

import traveler

RUN = "https://github.com/o/r/actions/runs/9"
SHA = "a" * 40
FACTORY = traveler.Traveler(
    run=RUN, target="a new branch from the head of `main`", sha=SHA, session="sesn_1", story=451
)
PLAIN = traveler.Traveler(run=RUN, target="`fix/x`", sha=SHA, session="sesn_2", pull_request=12)
BARE = traveler.Traveler(run=RUN, target=f"`repair/9`, from `{SHA}`", sha=SHA, session="sesn_3")


class RoundTripTests(unittest.TestCase):
    def test_parse_gives_back_each_rendered_traveler(self):
        for name, ticket in (("factory", FACTORY), ("plain", PLAIN), ("neither", BARE)):
            with self.subTest(name):
                self.assertEqual(traveler.parse(traveler.render(ticket)), ticket)

    def test_parse_reads_the_block_at_the_end_of_an_issue_body(self):
        body = f"The fix.\n\n## Scope\n\n- `a.yml`\n\n{traveler.render(FACTORY)}"
        self.assertEqual(traveler.parse(body), FACTORY)

    def test_the_block_starts_at_its_heading_and_ends_with_a_line_feed(self):
        text = traveler.render(PLAIN)
        self.assertTrue(text.startswith(f"{traveler.HEADING}\n\n"))
        self.assertTrue(text.endswith("`\n"))

    def test_a_field_that_parse_cannot_read_back_is_refused(self):
        for name, fields in (
            ("a line feed", {"target": "a\nb"}),
            ("a space in the run", {"run": "https://x y"}),
            ("a backtick in the session", {"session": "a`b"}),
            ("an empty head SHA", {"sha": ""}),
            ("a line separator", {"target": "a\u2028b"}),
            ("a target that ends like a pull request", {"target": "`x` (pull request #5)"}),
            ("a negative story", {"story": -1}),
        ):
            with self.subTest(name), self.assertRaises(ValueError):
                traveler.render(dataclasses.replace(BARE, **fields))


class ParseTests(unittest.TestCase):
    def test_a_body_with_no_block_gives_none(self):
        for body in (None, "", "The fix.\n\n## Scope\n\n- `a.yml`\n"):
            with self.subTest(body=body):
                self.assertIsNone(traveler.parse(body))


class ClosingIssueTests(unittest.TestCase):
    def test_a_line_closes_alone_gives_its_issue(self):
        self.assertEqual(traveler.closing_issue("The fix.\n\nCloses #5\n"), 5)

    def test_a_body_with_no_line_gives_none(self):
        for body in (None, "", "Part of #5\n"):
            with self.subTest(body=body):
                self.assertIsNone(traveler.closing_issue(body))

    def test_a_line_inside_a_sentence_gives_none(self):
        self.assertIsNone(traveler.closing_issue("This change Closes #5 in passing.\n"))


if __name__ == "__main__":
    unittest.main()
