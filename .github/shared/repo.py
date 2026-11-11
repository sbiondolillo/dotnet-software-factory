"""One repo on GitHub, over the adapter of github.py: each `gh` argument list that the judge, verdict.py and
comments.py use.

Each method answers one question that a caller asks. It owns the argument list, the field list, the parse of the
answer and the paging. A rule of the judge or of the repair loop stays in its caller. An adapter holds one token, so a
job with two tokens builds two Repo objects. A test of a caller hands down the FakeRepo of fake_repo.py, and a test of
Repo hands down a FakeGitHub of fake_github.py.
"""

import json

import closing

# The fields of a pull request that the judge reads from `gh pr view`.
PULL_REQUEST_FIELDS = f"number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url,{closing.FIELD}"


def comments_path(name, number, sha=None):
    """The REST path of the comments of a target of the repo `name`. The target is the issue or pull request `number`,
    since GitHub serves the comments of a pull request as those of an issue. With `number` None, the target is the
    commit `sha`. With neither, it raises ValueError."""
    if number is not None:
        return f"repos/{name}/issues/{number}/comments"
    if sha is None:
        raise ValueError("comments: give an issue or pull request number, or a commit SHA")
    return f"repos/{name}/commits/{sha}/comments"


class Repo:
    """The repo `name`, such as `owner/repo`, through the adapter `gh`."""

    def __init__(self, gh, name):
        self.gh = gh
        self.name = name

    def pull_request(self, number):
        """The pull request with PULL_REQUEST_FIELDS, from `gh pr view`."""
        return json.loads(self.gh.run("pr", "view", str(number), "-R", self.name, "--json", PULL_REQUEST_FIELDS))

    def rest_pull_request(self, number):
        """The pull request as the REST API gives it. After a push, its `head.sha` and the `headRefOid` of `gh pr view`
        change at different times, so the judge keeps both reads."""
        return json.loads(self.gh.run("api", f"repos/{self.name}/pulls/{number}"))

    def issue(self, number):
        """The issue with its `number`, `title` and `body`."""
        return json.loads(self.gh.run("issue", "view", str(number), "-R", self.name, "--json", "number,title,body"))

    def commits(self, number):
        """Each commit of the pull request, every page."""
        return self.gh.api_list(f"repos/{self.name}/pulls/{number}/commits")

    def commit(self, sha):
        """The commit as the REST API gives it, with its `files` and its `stats`."""
        return json.loads(self.gh.run("api", f"repos/{self.name}/commits/{sha}"))

    def files(self, number):
        """Each changed file of the pull request, every page."""
        return self.gh.api_list(f"repos/{self.name}/pulls/{number}/files")

    def comments(self, number, sha=None):
        """Each comment of the issue or pull request `number`, or of the commit `sha` when `number` is None, every
        page."""
        return self.gh.api_list(comments_path(self.name, number, sha))

    def post_comment(self, number, body, sha=None):
        """Posts `body` as a comment on the issue or pull request `number`, or on the commit `sha` when `number` is
        None. A commit has no `gh` subcommand for a comment, so one REST call serves each target."""
        text = json.dumps({"body": body})
        self.gh.run("api", "-X", "POST", comments_path(self.name, number, sha), "--input", "-", input_text=text)

    def post_check(self, name, sha, conclusion, title, summary):
        """Posts a completed check run named `name` on the commit. `conclusion` is `success` or `failure`."""
        self.gh.run(
            "api",
            f"repos/{self.name}/check-runs",
            "-f",
            f"name={name}",
            "-f",
            f"head_sha={sha}",
            "-f",
            "status=completed",
            "-f",
            f"conclusion={conclusion}",
            "-f",
            f"output[title]={title}",
            "-f",
            f"output[summary]={summary}",
        )

    def latest_check(self, name, sha):
        """The latest check run named `name` on the commit: (status, conclusion), or None when the commit has none."""
        answer = json.loads(
            self.gh.run("api", f"repos/{self.name}/commits/{sha}/check-runs?check_name={name}&filter=latest")
        )
        checks = answer.get("check_runs") or []
        return (checks[0]["status"], checks[0].get("conclusion")) if checks else None
