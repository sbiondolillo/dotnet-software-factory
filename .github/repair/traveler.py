"""The traveler of a repair issue: the block under `## Run` that read.py writes at the end of the issue body, and that
build.py and merge.py read back in later jobs. This module writes it and reads it, and knows only text: it
calls GitHub never. GLOSSARY.md defines the term.

The block has two forms. A factory repair has a line `Story: #<n>`. A plain repair whose red run is on the branch of
an open pull request has `(pull request #<n>)` at the end of its target line.
"""

import re
from dataclasses import dataclass

HEADING = "## Run"
HEADINGS = re.compile(r"#{1,6}\s")
TARGET = re.compile(r"^Target branch: (?P<target>.+?)(?: \(pull request #(?P<pr>\d+)\))?$")
SHA = re.compile(r"^Head SHA: `(?P<sha>[^`]+)`$")
RUN = re.compile(r"^Run: (?P<run>\S+)$")
STORY = re.compile(r"^Story: #(?P<story>\d+)$")
SESSION = re.compile(r"^Session: `(?P<session>[^`]+)`$")


@dataclass(frozen=True)
class Traveler:
    """The fields of the block. `run` is the URL of the red run, `target` the text of the target branch, `story` the
    number of the story of a factory repair, and `pull_request` the number of the pull request of the target branch.
    """

    run: str
    target: str
    sha: str
    session: str
    story: int | None = None
    pull_request: int | None = None


def render(traveler):
    """The block, from its heading `## Run` to its last line feed. read.py appends it to the body it files."""
    texts = (traveler.run, traveler.target, traveler.sha, traveler.session)
    numbers = (traveler.story, traveler.pull_request)
    if (
        any(text != text.strip() or text.splitlines() != [text] for text in texts)
        or any(c.isspace() for c in traveler.run)
        or "`" in traveler.sha + traveler.session
        or TARGET.match(f"Target branch: {traveler.target}")["pr"] is not None
        or any(number is not None and number < 0 for number in numbers)
    ):
        raise ValueError(f"traveler: a field that parse cannot read back: {traveler}")
    pull_request = f" (pull request #{traveler.pull_request})" if traveler.pull_request is not None else ""
    story = f"Story: #{traveler.story}\n\n" if traveler.story is not None else ""
    return (
        f"{HEADING}\n\nTarget branch: {traveler.target}{pull_request}\n\nHead SHA: `{traveler.sha}`\n\n"
        f"Run: {traveler.run}\n\n{story}Session: `{traveler.session}`\n"
    )


def block(body):
    """The lines under the last heading `## Run` of the body, up to the next heading, or None with no such heading."""
    lines = [line.rstrip() for line in (body or "").splitlines()]
    starts = [i for i, line in enumerate(lines) if line == HEADING]
    if not starts:
        return None
    rest = lines[starts[-1] + 1 :]
    end = next((i for i, line in enumerate(rest) if HEADINGS.match(line)), len(rest))
    return rest[:end]


def field(lines, pattern):
    return next((match for match in map(pattern.match, lines) if match), None)


def parse(body):
    """The Traveler of an issue body, or None when the body holds no block with a target, a head SHA, a run and a
    session."""
    lines = block(body)
    if lines is None:
        return None
    target, sha, run, session = (field(lines, pattern) for pattern in (TARGET, SHA, RUN, SESSION))
    if target is None or sha is None or run is None or session is None:
        return None
    story = field(lines, STORY)
    return Traveler(
        run=run["run"],
        target=target["target"],
        sha=sha["sha"],
        session=session["session"],
        story=int(story["story"]) if story else None,
        pull_request=int(target["pr"]) if target["pr"] else None,
    )
