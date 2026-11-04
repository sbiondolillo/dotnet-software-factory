"""The Managed Agents sessions of the judge and the repair loop: start a session, wait for it, and read its Report.

A Report is the JSON object an agent gives as its last message, as GLOSSARY.md states. The caller states its shape
twice: as a parse function, which takes the text of the last agent message and returns the Report or raises
ValueError with the fault, and as text for the agent. A parse of a JSON Report starts with `json_object`, the one
search for the object in the message. A message that fails the parse gets one correction turn in the same session,
which sends the fault and the shape and asks for the Report alone, as the factory sends a follow-up turn in
src/Core/ManagedAgentsAgent.cs.

Every request goes through the Anthropic adapter of anthropic.py, which the caller hands to `Session.start`. A session
that cannot give a Report raises SessionError, and the caller decides what the error means: the judge rejects the
angle, and the reader ends the job.
"""

import json
import re
import time
import urllib.parse
from dataclasses import dataclass

POLL = 15
ENDED = ("terminated", "error")
CORRECTION = (
    "The job rejected your last message. The JSON parser says: {fault}\n\n"
    "Send the corrected JSON object alone, with every field of your answer.\n\n"
    "{shape}"
)


class SessionError(Exception):
    """A session that gave no Report. `reason` says why, such as `refused`, `not idle after 1800 s`, `ended as error`
    or `the parser says: ...`. `text` is the last agent message, or the empty string."""

    def __init__(self, sid, reason, text=""):
        self.sid = sid
        self.reason = reason
        self.text = text
        super().__init__(f"session {sid}: {reason}")


@dataclass
class Answer:
    """The Report of a session. `value` is what the parse function returned, `usage` the last `session.usage` of the
    session, for the cost line, and `corrected` True when the Report came from the correction turn."""

    value: object
    usage: dict
    corrected: bool = False


def json_object(text):
    """The JSON object in an agent message: the text from its first `{` to its last `}`, or the whole text when it
    holds no such span. A text that is no JSON raises ValueError."""
    match = re.search(r"\{.*\}", text, re.S)
    return json.loads(match.group(0) if match else text)


def repository(repo, token):
    """The `github_repository` resource of a session: the repo `owner/name` with a read-only token, mounted at
    /workspace/<name>."""
    return {
        "type": "github_repository",
        "url": f"https://github.com/{repo}",
        "mount_path": f"/workspace/{repo.split('/')[1]}",
        "authorization_token": token,
    }


def last_text(events):
    """The last text of the newest agent message among events newest first, or the empty string."""
    return next(
        (
            c["text"]
            for e in events
            if e.get("type") == "agent.message"
            for c in reversed(e.get("content", []))
            if c.get("type") == "text"
        ),
        "",
    )


def last_stop(events):
    """The stop of the newest idle among events newest first, or None. The API names it `stop_reason` or
    `stop_details`, and StopReason of src/Core/ManagedAgentsAgent.cs reads both, in that order."""
    idle = next((e for e in events if e.get("type") == "session.status_idle"), None)
    if idle is None:
        return None
    return next((idle[name] for name in ("stop_reason", "stop_details") if isinstance(idle.get(name), dict)), None)


def nap(deadline, poll):
    """Sleep `poll` seconds, or less when the deadline comes first, so no read waits past the deadline."""
    time.sleep(max(0, min(poll, deadline - time.time())))


class Session:
    """One Managed Agents session, by its id `sid`, over the adapter `api`."""

    def __init__(self, api, sid):
        self.api = api
        self.sid = sid
        self.started = time.time()

    @classmethod
    def start(
        cls, api, agent, environment, title, prompt, metadata, budget_cents, resources=None, overrides=None
    ) -> "Session":
        """Post a session whose first event is the user.message `prompt`, under a budget in US cents. `overrides`
        replaces fields of the agent for this session alone, such as its tools."""
        body = {
            "agent": {"type": "agent_with_overrides", "id": agent, **overrides} if overrides else agent,
            "environment_id": environment,
            "title": title,
            "metadata": metadata,
            "budget": {"type": "limit", "max_list_cost": {"amount": str(budget_cents), "currency": "USD"}},
            "initial_events": [{"type": "user.message", "content": [{"type": "text", "text": prompt}]}],
        }
        if resources:
            body["resources"] = resources
        return cls(api, api.post("/v1/sessions", body)["id"])

    def events(self):
        """Every event of the session, newest first. A long session fills more than one page, so the read follows
        `next_page` back as ReadBackAsync of src/Core/ManagedAgentsAgent.cs does, one page at a time."""
        page = None
        while True:
            cursor = "" if page is None else f"&page={urllib.parse.quote(page, safe='')}"
            answer = self.api.get(f"/v1/sessions/{self.sid}/events?order=desc&limit=100{cursor}")
            yield from answer.get("data") or []
            page = answer.get("next_page")
            if not page:
                return

    def turn(self):
        """The id of the newest user.message, the events of its turn newest first, and the rest of the events after
        that message. The id is None when the events end first."""
        stream = self.events()
        events = []
        for e in stream:
            if e.get("type") == "user.message":
                return e.get("id"), events, stream
            events.append(e)
        return None, events, stream

    def status(self, deadline, poll):
        """The status of the session once it is idle, terminated or error. A session that is none of these at the
        deadline raises SessionError."""
        while True:
            status = self.api.get(f"/v1/sessions/{self.sid}")["status"]
            if status == "idle" or status in ENDED:
                return status
            if time.time() >= deadline:
                raise SessionError(self.sid, f"not idle after {int(time.time() - self.started)} s")
            nap(deadline, poll)

    def correction_turn(self, before, deadline, poll):
        """The events and the rest of the turn of a user.message posted to an idle session. `before` is the id of the
        newest user.message before the POST. Right after the POST the listing can still end at that message and its
        old idle, so the turn ends at an idle event after a user.message with another id, as in WaitForTurnAsync of
        src/Core/ManagedAgentsAgent.cs. A session that ends as terminated or error goes idle no more."""
        while True:
            status = self.status(deadline, poll)
            message, events, rest = self.turn()
            if message != before and any(e.get("type") == "session.status_idle" for e in events):
                return events, rest
            if status in ENDED:
                raise SessionError(
                    self.sid, f"ended as {status} before its correction turn went idle", last_text(events)
                )
            if time.time() >= deadline:
                raise SessionError(
                    self.sid, f"the correction turn is not idle after {int(time.time() - self.started)} s"
                )
            nap(deadline, poll)

    def checked(self, events):
        """The last agent message of a turn. A turn that stopped on a refusal raises SessionError."""
        text = last_text(events)
        stop = last_stop(events)
        if stop and stop.get("type") == "refusal":
            raise SessionError(self.sid, "refused", text)
        return text

    def answer(self, value, events, rest, corrected):
        """The Answer of a Report, with the newest `session.usage`: in the turn, or else in the events before it."""
        usage = next((e.get("usage") for e in events if e.get("type") == "session.usage"), None)
        if usage is None:
            usage = next((e.get("usage") for e in rest if e.get("type") == "session.usage"), None)
        return Answer(value, usage or {}, corrected)

    def report(self, parse, shape, deadline, poll=POLL) -> Answer:
        """The Report of the session's first turn, read once the session is idle. `deadline` is a time.time() value,
        and the wait polls every `poll` seconds until it. When `parse` raises ValueError, one correction turn sends
        the fault and `shape`, and its message is parsed again. A second fault, a refusal, a session that ended as
        terminated or error before the turn it needs, or a deadline that passes raises SessionError."""
        status = self.status(deadline, poll)
        before, events, rest = self.turn()
        text = self.checked(events)
        try:
            value = parse(text)
        except ValueError as error:
            first = str(error)
        else:
            return self.answer(value, events, rest, False)
        if status not in ENDED:
            status = self.api.get(f"/v1/sessions/{self.sid}")["status"]
        if status in ENDED:
            raise SessionError(self.sid, f"ended as {status} after its first turn; the parser says: {first}", text)
        print(f"sessions: session {self.sid}: the parser says: {first}; one correction turn")
        message = {
            "type": "user.message",
            "content": [{"type": "text", "text": CORRECTION.format(fault=first, shape=shape)}],
        }
        self.api.post(f"/v1/sessions/{self.sid}/events", {"events": [message]})
        events, rest = self.correction_turn(before, deadline, poll)
        text = self.checked(events)
        try:
            value = parse(text)
        except ValueError as error:
            raise SessionError(self.sid, f"the parser says: first {first}; then {error}", text) from error
        return self.answer(value, events, rest, True)
