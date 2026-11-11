"""The fake of Repo of repo.py, for the unit tests of the judge, the repair loop, verdict.py and comments.py.

It holds scripted answers and no rule of GitHub: a write changes nothing in it. It writes out no method of Repo. Each
call resolves its name on Repo and binds its arguments against the signature of that method, so a name that Repo
lacks raises AttributeError and a wrong argument list raises TypeError, in the caller's test.
"""

import inspect

from fake_github import Fail
from github import GitHubError
from repo import Repo


def method(name):
    """The method `name` of Repo, or AttributeError."""
    found = getattr(Repo, name, None)
    if not inspect.isfunction(found):
        raise AttributeError(f"Repo has no method {name!r}")
    return found


def arguments(name, args, kwargs):
    """The arguments of a call of the method `name`, by parameter, with each default filled in. A wrong argument list
    raises TypeError."""
    bound = inspect.signature(method(name)).bind(None, *args, **kwargs)
    bound.apply_defaults()
    return dict(list(bound.arguments.items())[1:])


class FakeRepo:
    """The surface of repo.Repo for the repo `name`.

    A test gives the outputs of a method for its arguments with `answer`. A call takes the next output of the latest
    answer whose arguments equal its own, and the last output answers each later call. An argument of
    unittest.mock.ANY equals any value. An output is a value, a Fail, which raises GitHubError, or a function of no
    argument that gives one of those at the call. A call with no answer fails the test. `calls` holds each call as
    (name, arguments by parameter), in order."""

    def __init__(self, name="o/r"):
        self.name = name
        self.answers = []
        self.calls = []

    def answer(self, name, args, *outputs):
        """The outputs of the method `name` for the positional arguments `args`, in order."""
        self.answers.append((name, arguments(name, args, {}), list(outputs)))

    def calls_of(self, name):
        """The arguments of each call of the method `name`, by parameter, in order."""
        return [called for each, called in self.calls if each == name]

    def __getattr__(self, name):
        method(name)

        def call(*args, **kwargs):
            called = arguments(name, args, kwargs)
            self.calls.append((name, called))
            return self.next(name, called)

        return call

    def next(self, name, called):
        matches = [outputs for each, given, outputs in self.answers if each == name and given == called]
        if not matches:
            raise AssertionError(f"FakeRepo: no answer for {name}({called})")
        outputs = matches[-1]
        output = outputs.pop(0) if len(outputs) > 1 else outputs[0]
        output = output() if callable(output) else output
        if isinstance(output, Fail):
            command = (name, *map(str, called.values()))
            raise GitHubError(command, output.status, output.stdout, output.stderr, output.returncode)
        return output
