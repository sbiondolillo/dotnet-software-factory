"""The fake of the GitHub adapter of github.py, for the unit tests of repo.py. Any test can use `closes` and `Fail`.

A test fills a table: each call with exactly the arguments of an entry takes the entry's next output, and the last
output answers every later call. A call with no entry fails the test. The fake records every call in `calls`.
"""

import json

from closing import FIELD
from github import GitHubError


def closes(repo, *numbers):
    """The field `closingIssuesReferences` of a pull request that closes each issue number of `repo`, as gh answers
    it, to spread into the answer of `gh pr view`. With no number, the pull request closes no issue."""
    owner, name = repo.split("/")
    references = [
        {
            "id": f"I_{number}",
            "number": number,
            "repository": {"id": "R_1", "name": name, "owner": {"id": "U_1", "login": owner}},
            "url": f"https://github.com/{repo}/issues/{number}",
        }
        for number in numbers
    ]
    return {FIELD: references}


class Fail:
    """An output that raises GitHubError, as a gh call with the exit code `returncode` does."""

    def __init__(self, status=None, stdout="", stderr=None, returncode=1):
        self.status = status
        self.stdout = stdout
        self.stderr = stderr if stderr is not None else f"gh: failed (HTTP {status})" if status else "gh: failed"
        self.returncode = returncode


class FakeGitHub:
    """The surface of github.GitHub. An output is text, a Fail, any other value as its JSON text, or a function of
    no argument that gives one of those at the call. `calls` holds each call as (args, input_text), and a call of
    `api_list(path)` as (("api_list", path), None)."""

    def __init__(self, token="fake"):
        self.token = token
        self.answers = {}
        self.lists = {}
        self.calls = []

    def answer(self, args, *outputs):
        """The outputs of `run(*args)`, in order."""
        self.answers[tuple(args)] = list(outputs)

    def answer_list(self, path, *outputs):
        """The outputs of `api_list(path)`, in order, each a list of items or a Fail."""
        self.lists[path] = list(outputs)

    def run(self, *args, input_text=None):
        self.calls.append((args, input_text))
        output = self.next(self.answers, args, f"gh {' '.join(args)}")
        if isinstance(output, Fail):
            raise GitHubError(args, output.status, output.stdout, output.stderr, output.returncode)
        return output if isinstance(output, str) else json.dumps(output)

    def api_list(self, path):
        self.calls.append((("api_list", path), None))
        output = self.next(self.lists, path, f"api_list {path}")
        if isinstance(output, Fail):
            raise GitHubError(("api", path), output.status, output.stdout, output.stderr, output.returncode)
        return output

    def next(self, table, key, name):
        if key not in table:
            raise AssertionError(f"FakeGitHub: no answer for {name}")
        outputs = table[key]
        output = outputs.pop(0) if len(outputs) > 1 else outputs[0]
        return output() if callable(output) else output

    def args(self):
        """The args of each call, in order."""
        return [args for args, _ in self.calls]

    def count(self, *args):
        """The number of calls with exactly these arguments."""
        return self.args().count(args)

    def input_of(self, *args):
        """The input text of the last call with exactly these arguments."""
        return [text for called, text in self.calls if called == args][-1]
