"""One comment for each key of a caller: the judge and the repair loop post a comment once behind a hidden marker.

A marker is an HTML comment, such as `<!-- repair-merge:<sha> -->`, and the caller chooses its text, so the key is the
caller's: a SHA, or a SHA and a spec. GitHub renders the marker as nothing, and the next run of the step reads it in
the comments, so a second run on one key posts nothing.

A target is the issue or pull request `number`, or the commit `sha` when `number` is None, for a red run on main,
which has no pull request. `repo` is a Repo of repo.py, which reads and posts the comments of the target.
"""


def has(repo, number, marker, sha=None):
    """Whether a comment of the target holds `marker`. It reads every page of the comments."""
    return any(marker in (comment.get("body") or "") for comment in repo.comments(number, sha))


def post_once(repo, number, marker, body, sha=None):
    """Post `body` under `marker` on the target unless a comment there holds `marker`. True when posted.

    The posted comment is the marker on its own line, then the body."""
    if has(repo, number, marker, sha):
        return False
    repo.post_comment(number, f"{marker}\n{body}", sha)
    return True
