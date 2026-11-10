"""The check run judge-verdict: the judge posts it, and the repair loop reads it. Branch protection on main requires it.

The check has no identity other than its name. A job with `checks: write` can post a check run of this name, and
`latest` reads it by its name alone. The path rule of merge.py in .github/repair/ guards that.
"""

import json

CHECK = "judge-verdict"


def post(gh, repo, sha, conclusion, title, summary):
    """A completed check run named judge-verdict on the commit. `conclusion` is `success` or `failure`."""
    gh.run(
        "api",
        f"repos/{repo}/check-runs",
        "-f",
        f"name={CHECK}",
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


def latest(gh, repo, sha):
    """The latest check run named judge-verdict on the commit: (status, conclusion), or None when the judge posted
    none."""
    answer = json.loads(gh.run("api", f"repos/{repo}/commits/{sha}/check-runs?check_name={CHECK}&filter=latest"))
    checks = answer.get("check_runs") or []
    return (checks[0]["status"], checks[0].get("conclusion")) if checks else None
