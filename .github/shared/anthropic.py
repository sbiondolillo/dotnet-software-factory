"""The Anthropic adapter of the judge and the repair loop: the one place that sends a request to the Anthropic API.

It holds the key and sends each request, and nothing else. A session lives in sessions.py, over this adapter. A
script's `main` builds the adapter with `Anthropic.from_env()` and hands it to each session, so a test hands down the
FakeApi of fake_anthropic.py in its place.

The retry is that of SendCoreAsync in src/Core/ManagedAgentsClient.cs. A GET goes again on a connection error, a
timeout, a 429 or any 5xx. A POST goes again on a 429 or a 529 alone, since those say that the API did not process
it, so a retry never repeats a side effect. A request takes up to 4 attempts, and the wait before attempt n + 1 is
n times 5 seconds.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request

API = "https://api.anthropic.com"
BETA = "managed-agents-2026-04-01"
ATTEMPTS = 4
DELAY = 5
TIMEOUT = 60
BODY_LIMIT = 500


def cut(text, limit=BODY_LIMIT):
    """The text cut to `limit` bytes of UTF-8, with no broken character at the end."""
    return text.encode()[:limit].decode(errors="ignore")


class AnthropicError(Exception):
    """A request that failed after its last attempt. `status` is the HTTP status, or None for a connection error or a
    timeout. `body` is the answer text, or the error of the connection, cut to 500 bytes."""

    def __init__(self, method, path, status, body):
        self.method = method
        self.path = path
        self.status = status
        self.body = cut(body)
        super().__init__(f"{method} {path} -> {status if status is not None else 'no answer'}: {self.body}")


def again(method, status):
    """Whether an answer with this status takes another attempt."""
    if method == "GET":
        return status == 429 or status >= 500
    return status in (429, 529)


class Anthropic:
    """The API under one key. `sender` sends a urllib request as urllib.request.urlopen does."""

    def __init__(self, key, sender=urllib.request.urlopen):
        self.key = key
        self.sender = sender

    @classmethod
    def from_env(cls):
        """The adapter of the job's key, JUDGE_ANTHROPIC_API_KEY."""
        key = os.environ.get("JUDGE_ANTHROPIC_API_KEY") or sys.exit("anthropic: JUDGE_ANTHROPIC_API_KEY is not set")
        return cls(key)

    def get(self, path):
        """The JSON answer of `GET <path>`."""
        return self.send("GET", path)

    def post(self, path, body):
        """The JSON answer of `POST <path>` with the JSON `body`."""
        return self.send("POST", path, body)

    def request(self, method, path, body):
        return urllib.request.Request(
            API + path,
            data=json.dumps(body).encode() if body is not None else None,
            method=method,
            headers={
                "x-api-key": self.key,
                "anthropic-version": "2023-06-01",
                "anthropic-beta": BETA,
                "content-type": "application/json",
            },
        )

    def send(self, method, path, body=None):
        """The JSON answer of one request, after up to ATTEMPTS attempts. A failure of the last attempt, or of an
        attempt that takes no retry, raises AnthropicError."""
        for attempt in range(1, ATTEMPTS + 1):
            try:
                with self.sender(self.request(method, path, body), timeout=TIMEOUT) as response:
                    status, text = getattr(response, "status", 200), response.read().decode()
            except urllib.error.HTTPError as error:
                answer = error.read().decode(errors="replace")
                if attempt >= ATTEMPTS or not again(method, error.code):
                    raise AnthropicError(method, path, error.code, answer) from error
            except OSError as error:
                # urllib.error.URLError and a timeout are both OSError: the request got no answer.
                if method != "GET" or attempt >= ATTEMPTS:
                    raise AnthropicError(method, path, None, str(error)) from error
            else:
                try:
                    return json.loads(text) if text.strip() else {}
                except ValueError as error:
                    raise AnthropicError(method, path, status, text) from error
            time.sleep(DELAY * attempt)
        raise AssertionError("unreachable")
