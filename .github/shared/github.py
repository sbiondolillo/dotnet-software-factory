"""The GitHub adapter of the judge and the repair loop: the one place that runs `gh`, the GitHub CLI.

It wraps the CLI and nothing else. An operation of the repair loop and a retry live in the caller. Each adapter holds
one credential, and every function that talks to GitHub takes the adapter as a parameter. A script's `main` builds
it, so a test hands a FakeGitHub of fake_github.py down in its place.
"""

import json
import os
import re
import subprocess
import sys

# The text gh writes to standard error for a failed REST call, such as `gh: Not Found (HTTP 404)`.
STATUS = re.compile(r"\(HTTP (\d{3})\)")


class GitHubError(Exception):
    """A gh call that exited with a code other than 0. `status` is the HTTP status the CLI reported, or None."""

    def __init__(self, command, status, stdout, stderr, returncode=1):
        self.command = tuple(command)
        self.status = status
        self.stdout = stdout
        self.stderr = stderr
        self.returncode = returncode
        super().__init__(f"gh {' '.join(self.command[:3])} failed: {stderr.strip()[:500]}")


class GitHub:
    """The CLI under one token. `runner` runs a command as subprocess.run does."""

    def __init__(self, token, runner=subprocess.run):
        self.token = token
        self.runner = runner

    @classmethod
    def from_env(cls):
        """The adapter of the job's own token, GH_TOKEN."""
        token = os.environ.get("GH_TOKEN") or sys.exit("github: GH_TOKEN is not set")
        return cls(token)

    def run(self, *args, input_text=None):
        """The standard output of `gh <args>`, with the adapter's token as GH_TOKEN. An exit code other than 0 raises
        GitHubError, with the status read off standard error."""
        result = self.runner(
            ["gh", *args],
            capture_output=True,
            text=True,
            input=input_text,
            env={**os.environ, "GH_TOKEN": self.token},
        )
        if result.returncode != 0:
            match = STATUS.search(result.stderr or "")
            status = int(match.group(1)) if match else None
            raise GitHubError(args, status, result.stdout or "", result.stderr or "", result.returncode)
        return result.stdout

    def api_list(self, path):
        """Every item of a paged REST list, one JSON object for each line of the output."""
        lines = self.run("api", f"{path}?per_page=100", "--paginate", "--jq", ".[] | @json").splitlines()
        return [json.loads(line) for line in lines if line]
