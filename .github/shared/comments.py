"""One comment for each key of a caller: the repair loop posts a comment once behind a hidden marker.

A marker is an HTML comment, such as `<!-- repair-merge:<sha> -->`, and the caller chooses its text, so the key is the
caller's: a SHA, or a SHA and a spec. GitHub renders the marker as nothing, and the next run of the step reads it in
the comments, so a second run on one key posts nothing. The judge's marker comment is the same operation, and the judge
joins this module when #479 or a later task touches judge.py.

A target is the issue or pull request `number`, since GitHub serves the comments of a pull request as those of an
issue. With `number` None, the target is the commit `sha`, for a red run on main, which has no pull request.
"""

import json


def path(repo, number, sha=None):
    """The REST path of the comments of the target."""
    if number is not None:
        return f"repos/{repo}/issues/{number}/comments"
    if sha is None:
        raise ValueError("comments: give an issue or pull request number, or a commit SHA")
    return f"repos/{repo}/commits/{sha}/comments"


def has(gh, repo, number, marker, sha=None):
    """Whether a comment of the target holds `marker`. It reads every page of the comments."""
    return any(marker in (comment.get("body") or "") for comment in gh.api_list(path(repo, number, sha)))


def post_once(gh, repo, number, marker, body, sha=None):
    """Post `body` under `marker` on the target unless a comment there holds `marker`. True when posted.

    The posted comment is the marker on its own line, then the body."""
    if has(gh, repo, number, marker, sha):
        return False
    text = json.dumps({"body": f"{marker}\n{body}"})
    gh.run("api", "-X", "POST", path(repo, number, sha), "--input", "-", input_text=text)
    return True
