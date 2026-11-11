"""One repo on GitHub, over the adapter of github.py: the one module that calls the adapter. It holds each `gh` argument
list that the judge, the repair loop, verdict.py and comments.py use.

Each method answers one question that a caller asks. It owns the argument list, the field list, the parse of the
answer, the paging, and how `gh` or GitHub answers a special case. A rule of the judge or of the repair loop stays in
its caller: a message, an exit, a retry, a wait and a decision. An adapter holds one token, so a job with two tokens
builds two Repo objects. A test of a caller hands down the FakeRepo of fake_repo.py, and a test of Repo hands down the
fake adapter of fake_github.py.
"""

import json
import re

import closing
from github import GitHubError

# The fields of a pull request that the judge and the repair loop read from `gh pr view`.
PULL_REQUEST_FIELDS = (
    "number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url,state,labels,mergeCommit,mergedBy,changedFiles,"
    + closing.FIELD
)
# The fields of an issue that the judge and the repair loop read from `gh issue view`.
ISSUE_FIELDS = "number,title,body,state,labels,comments"
# The fields of a workflow run that the repair loop reads from `gh run list`, and the count of runs it lists.
RUN_FIELDS = "databaseId,displayTitle,headSha,status,conclusion,url,createdAt"
RUN_LIMIT = "30"


def _comments_path(name, number, sha=None):
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

    # The repo and its branches.

    def default_branch(self):
        """The name of the default branch of the repo."""
        return self.gh.run("api", f"repos/{self.name}", "--jq", ".default_branch").strip()

    def file_at(self, path, sha):
        """The text of the file at the commit, or None when the commit holds no such file: GitHub answers 404 for it.
        Any other failure raises GitHubError."""
        try:
            return self.gh.run(
                "api", "-H", "Accept: application/vnd.github.raw+json", f"repos/{self.name}/contents/{path}?ref={sha}"
            )
        except GitHubError as error:
            if error.status == 404:
                return None
            raise

    def has_branch(self, branch):
        """Whether the branch exists. GitHub answers 404 for a branch it lacks. Any other failure raises GitHubError."""
        try:
            self.gh.run("api", f"repos/{self.name}/git/ref/heads/{branch}")
        except GitHubError as error:
            if error.status == 404:
                return False
            raise
        return True

    def create_branch(self, branch, sha):
        """Creates the branch at the commit."""
        self.gh.run(
            "api", "-X", "POST", f"repos/{self.name}/git/refs", "-f", f"ref=refs/heads/{branch}", "-f", f"sha={sha}"
        )

    def move_branch(self, branch, sha):
        """Moves the branch to the commit, with a forced update, so the commit need not descend from the branch."""
        self.gh.run(
            "api", "-X", "PATCH", f"repos/{self.name}/git/refs/heads/{branch}", "-f", f"sha={sha}", "-F", "force=true"
        )

    def delete_branch(self, branch):
        """Deletes the branch. A branch that GitHub lacks raises GitHubError, as any other failure does."""
        self.gh.run("api", "-X", "DELETE", f"repos/{self.name}/git/refs/heads/{branch}")

    def delete_branch_if_present(self, branch):
        """Deletes the branch when it exists, and gives whether it did. A delete of a ref that GitHub lacks fails, so
        the delete runs only for a ref that the list of matching refs holds. That list matches by prefix, so the full
        name of the ref decides."""
        refs = json.loads(self.gh.run("api", f"repos/{self.name}/git/matching-refs/heads/{branch}"))
        if f"refs/heads/{branch}" not in [ref["ref"] for ref in refs]:
            return False
        self.delete_branch(branch)
        return True

    # Pull requests.

    def pull_request(self, number):
        """The pull request with PULL_REQUEST_FIELDS, from `gh pr view`."""
        return json.loads(self.gh.run("pr", "view", str(number), "-R", self.name, "--json", PULL_REQUEST_FIELDS))

    def rest_pull_request(self, number):
        """The pull request as the REST API gives it. After a push, its `head.sha` and the `headRefOid` of `gh pr view`
        change at different times, so the judge keeps both reads."""
        return json.loads(self.gh.run("api", f"repos/{self.name}/pulls/{number}"))

    def open_pull_request(self, branch):
        """The open pull request whose head is the branch, with its `number`, `headRefOid` and `labels`, or None."""
        pulls = json.loads(
            self.gh.run(
                "pr", "list", "-R", self.name, "--head", branch, "--state", "open", "--json", "number,headRefOid,labels"
            )
        )
        return pulls[0] if pulls else None

    def commits(self, number):
        """Each commit of the pull request, every page."""
        return self.gh.api_list(f"repos/{self.name}/pulls/{number}/commits")

    def commit(self, sha):
        """The commit as the REST API gives it, with its `files` and its `stats`."""
        return json.loads(self.gh.run("api", f"repos/{self.name}/commits/{sha}"))

    def files(self, number):
        """Each changed file of the pull request, every page. Each entry is parsed whole, so a path that holds a line
        feed stays one path. The API names both paths of a rename, `filename` and `previous_filename`. `gh pr diff`
        is no route: it reads the diff media type, which GitHub refuses for a large diff. The API lists at most 3000
        files, so a caller compares the length of the list with `changedFiles` of the pull request."""
        return self.gh.api_list(f"repos/{self.name}/pulls/{number}/files")

    def label_pull_request(self, number, label):
        """Adds the label to the pull request."""
        self.gh.run("pr", "edit", str(number), "-R", self.name, "--add-label", label)

    def enable_auto_merge(self, number, sha):
        """Arms GitHub's auto-merge of the pull request: a squash that deletes the head branch, and only while the head
        is the commit `sha`."""
        self.gh.run(
            "pr",
            "merge",
            str(number),
            "-R",
            self.name,
            "--auto",
            "--squash",
            "--delete-branch",
            "--match-head-commit",
            sha,
        )

    def disable_auto_merge(self, number):
        """Disarms GitHub's auto-merge of the pull request."""
        self.gh.run("pr", "merge", str(number), "-R", self.name, "--disable-auto")

    # Issues.

    def issue(self, number):
        """The issue with ISSUE_FIELDS, from `gh issue view`."""
        return json.loads(self.gh.run("issue", "view", str(number), "-R", self.name, "--json", ISSUE_FIELDS))

    def issues_with_label(self, label):
        """Each issue with the label, open or closed, up to 200, with its `number`, `body`, `state` and `labels`."""
        return json.loads(
            self.gh.run(
                "issue",
                "list",
                "-R",
                self.name,
                "--label",
                label,
                "--state",
                "all",
                "--limit",
                "200",
                "--json",
                "number,body,state,labels",
            )
        )

    def open_issues_with_labels(self, labels):
        """Each open issue with every label of `labels`, with its `number`."""
        flags = [arg for label in labels for arg in ("--label", label)]
        return json.loads(self.gh.run("issue", "list", "-R", self.name, *flags, "--state", "open", "--json", "number"))

    def create_issue(self, title, body, labels):
        """Creates an issue with each label of `labels`, and gives its URL. `gh issue create` can exit with the code 0
        and print no URL, and then the text is empty. The body goes on standard input."""
        args = ["issue", "create", "-R", self.name, "--title", title]
        for label in labels:
            args += ["--label", label]
        return self.gh.run(*args, "--body-file", "-", input_text=body).strip()

    def close_issue(self, number, comment):
        """Closes the issue with the comment."""
        self.gh.run("issue", "close", str(number), "-R", self.name, "--comment", comment)

    # Comments, on an issue, a pull request or a commit.

    def comments(self, number, sha=None):
        """Each comment of the issue or pull request `number`, or of the commit `sha` when `number` is None, every
        page."""
        return self.gh.api_list(_comments_path(self.name, number, sha))

    def post_comment(self, number, body, sha=None):
        """Posts `body` as a comment on the issue or pull request `number`, or on the commit `sha` when `number` is
        None. A commit has no `gh` subcommand for a comment, so one REST call serves each target."""
        text = json.dumps({"body": body})
        self.gh.run("api", "-X", "POST", _comments_path(self.name, number, sha), "--input", "-", input_text=text)

    # Check runs.

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

    # Workflow runs.

    def run_url(self, run_id):
        """The web address of the workflow run. It sends no call."""
        return f"https://github.com/{self.name}/actions/runs/{run_id}"

    def run_id_of(self, url):
        """The id of the workflow run at the web address `url`, or None for an address of another repo or of no run.
        It sends no call."""
        match = re.fullmatch(rf"https://github\.com/{re.escape(self.name)}/actions/runs/(\d+)", url or "")
        return int(match.group(1)) if match else None

    def workflow_run(self, run_id):
        """The workflow run as the REST API gives it."""
        return json.loads(self.gh.run("api", f"repos/{self.name}/actions/runs/{run_id}"))

    def workflow_runs(self, workflow, branch=None, status=None, event=None):
        """The latest RUN_LIMIT runs of the workflow, newest first, with RUN_FIELDS: on the branch, with the status and
        of the event. Each filter left out matches any run."""
        flags = [
            arg
            for flag, value in (("--branch", branch), ("--status", status), ("--event", event))
            if value
            for arg in (flag, value)
        ]
        return json.loads(
            self.gh.run(
                "run",
                "list",
                "-R",
                self.name,
                "--workflow",
                workflow,
                *flags,
                "--limit",
                RUN_LIMIT,
                "--json",
                RUN_FIELDS,
            )
        )

    def run_status(self, run_id):
        """The `status` and the `conclusion` of the workflow run, from `gh run view`."""
        return json.loads(self.gh.run("run", "view", str(run_id), "-R", self.name, "--json", "status,conclusion"))

    def failed_log(self, run_id, attempt):
        """The log of each failed job of the attempt of the run.

        A failed job with 0 steps, such as a failed judge-verdict, has no log. `gh run view --log-failed` then prints
        the log of the other jobs and exits with the code 1 and `log not found`, as #392 shows. So a failure with the
        code 1 and output other than whitespace gives that output. Any other failure raises GitHubError.

        It gives the log, and then None for a clean read, or the standard error of `gh` when the log is the output of
        such a failure, so that a caller can say so."""
        try:
            log = self.gh.run("run", "view", str(run_id), "-R", self.name, "--attempt", str(attempt), "--log-failed")
        except GitHubError as error:
            if error.returncode != 1 or not error.stdout.strip():
                raise
            return error.stdout, error.stderr
        return log, None

    def rerun_failed(self, run_id):
        """Reruns the failed jobs of the workflow run, as a new attempt of the same run."""
        self.gh.run("run", "rerun", str(run_id), "-R", self.name, "--failed")

    def dispatch_workflow(self, workflow, ref=None, inputs=None):
        """Dispatches the workflow at the ref, or at the default branch with none, with each input as
        `-f name=value`, in order."""
        at = ["--ref", ref] if ref else []
        fields = [arg for name, value in (inputs or {}).items() for arg in ("-f", f"{name}={value}")]
        self.gh.run("workflow", "run", workflow, "-R", self.name, *at, *fields)
