"""The check run judge-verdict: the judge posts it, and the repair loop reads it. Branch protection on main requires it.

The check has no identity other than its name. A job with `checks: write` can post a check run of this name, and
`latest` reads it by its name alone. The path rule of merge.py in .github/repair/ guards that. `repo` is a Repo of
repo.py, which posts and reads the check run.
"""

CHECK = "judge-verdict"


def post(repo, sha, conclusion, title, summary):
    """A completed check run named judge-verdict on the commit. `conclusion` is `success` or `failure`."""
    repo.post_check(CHECK, sha, conclusion, title, summary)


def latest(repo, sha):
    """The latest check run named judge-verdict on the commit: (status, conclusion), or None when the judge posted
    none."""
    return repo.latest_check(CHECK, sha)
