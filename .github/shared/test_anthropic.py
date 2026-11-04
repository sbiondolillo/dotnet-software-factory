"""Unit tests of anthropic.py, for #518. The build job of ci.yml runs them with
`python3 -B -m unittest discover -s .github/shared -p 'test_*.py'`. A fake sender stands in for urlopen, so no test
calls the API, and time.sleep is replaced, so no test waits."""

import io
import json
import os
import unittest
import urllib.error
from unittest import mock

import anthropic
from anthropic import Anthropic, AnthropicError


class Response(io.BytesIO):
    """An answer of urlopen: a context manager whose read gives the body."""

    status = 200


class Sender:
    """urlopen for the API: each call takes the next answer and is recorded. An answer is a status and a body, or an
    exception to raise."""

    def __init__(self, *answers):
        self.answers = list(answers)
        self.requests = []

    def __call__(self, request, timeout=None):
        self.requests.append((request, timeout))
        answer = self.answers.pop(0)
        if isinstance(answer, Exception):
            raise answer
        status, body = answer
        if status >= 400:
            raise urllib.error.HTTPError(request.full_url, status, "error", {}, io.BytesIO(body.encode()))
        return Response(body.encode())


class SendTests(unittest.TestCase):
    def send(self, method, *answers):
        sender = Sender(*answers)
        api = Anthropic("the-key", sender)
        with mock.patch.object(anthropic.time, "sleep") as sleep:
            try:
                result = api.get("/v1/x") if method == "GET" else api.post("/v1/x", {"a": 1})
                return result, None, sender, sleep
            except AnthropicError as error:
                return None, error, sender, sleep

    def test_a_get_that_answers_500_then_200_returns_the_200(self):
        result, error, sender, sleep = self.send("GET", (500, "busy"), (200, '{"ok": true}'))
        self.assertIsNone(error)
        self.assertEqual(result, {"ok": True})
        self.assertEqual(len(sender.requests), 2)
        self.assertEqual([call.args for call in sleep.call_args_list], [(anthropic.DELAY,)])

    def test_a_post_that_answers_500_raises_and_sends_once(self):
        _, error, sender, _ = self.send("POST", (500, "busy"), (200, "{}"))
        self.assertEqual(error.status, 500)
        self.assertEqual(error.body, "busy")
        self.assertEqual(len(sender.requests), 1)

    def test_a_post_that_answers_429_then_200_returns_the_200(self):
        result, error, sender, _ = self.send("POST", (429, "slow down"), (200, '{"id": "s"}'))
        self.assertIsNone(error)
        self.assertEqual(result, {"id": "s"})
        self.assertEqual(len(sender.requests), 2)

    def test_a_post_that_answers_529_goes_again(self):
        result, _, sender, _ = self.send("POST", (529, "overloaded"), (200, "{}"))
        self.assertEqual(result, {})
        self.assertEqual(len(sender.requests), 2)

    def test_a_get_that_fails_4_times_raises(self):
        _, error, sender, sleep = self.send("GET", *[(503, "down")] * 4)
        self.assertEqual(error.status, 503)
        self.assertEqual(len(sender.requests), anthropic.ATTEMPTS)
        self.assertEqual([call.args for call in sleep.call_args_list], [(5,), (10,), (15,)])

    def test_a_get_goes_again_on_a_connection_error_and_a_timeout(self):
        refused = urllib.error.URLError("refused")
        result, _, sender, _ = self.send("GET", refused, TimeoutError("timed out"), (200, '{"n": 1}'))
        self.assertEqual(result, {"n": 1})
        self.assertEqual(len(sender.requests), 3)

    def test_a_connection_error_on_a_post_raises_at_once(self):
        _, error, sender, _ = self.send("POST", urllib.error.URLError("refused"), (200, "{}"))
        self.assertIsNone(error.status)
        self.assertIn("refused", error.body)
        self.assertEqual(len(sender.requests), 1)

    def test_a_404_on_a_get_raises_at_once(self):
        _, error, sender, _ = self.send("GET", (404, "not found"), (200, "{}"))
        self.assertEqual(error.status, 404)
        self.assertEqual(len(sender.requests), 1)
        self.assertEqual(str(error), "GET /v1/x -> 404: not found")

    def test_the_body_of_an_error_is_cut_to_500_bytes(self):
        _, error, _, _ = self.send("GET", (400, "é" * 400))
        self.assertLessEqual(len(error.body.encode()), 500)
        self.assertEqual(error.body, "é" * 250)

    def test_the_key_is_in_the_header_of_each_request(self):
        _, _, sender, _ = self.send("GET", (500, ""), (200, "{}"))
        for request, timeout in sender.requests:
            self.assertEqual(request.get_header("X-api-key"), "the-key")
            self.assertEqual(request.get_header("Anthropic-beta"), anthropic.BETA)
            self.assertEqual(timeout, anthropic.TIMEOUT)

    def test_a_post_sends_its_body_as_json(self):
        _, _, sender, _ = self.send("POST", (200, ""))
        request, _ = sender.requests[0]
        self.assertEqual(request.get_method(), "POST")
        self.assertEqual(request.full_url, anthropic.API + "/v1/x")
        self.assertEqual(json.loads(request.data), {"a": 1})


class FromEnvTests(unittest.TestCase):
    def test_the_key_comes_from_the_environment_once(self):
        with mock.patch.dict(os.environ, {"JUDGE_ANTHROPIC_API_KEY": "k1"}):
            api = Anthropic.from_env()
        self.assertEqual(api.key, "k1")

    def test_no_key_exits(self):
        with mock.patch.dict(os.environ, {}, clear=True), self.assertRaises(SystemExit) as stop:
            Anthropic.from_env()
        self.assertIn("JUDGE_ANTHROPIC_API_KEY", stop.exception.code)


if __name__ == "__main__":
    unittest.main()
