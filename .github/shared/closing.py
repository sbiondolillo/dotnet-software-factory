"""The issue that a pull request closes, as GitHub reports it. The judge and the repair loop parse no closing keyword.

GitHub lists each issue that the merge of a pull request closes under `closingIssuesReferences`: an issue that a
closing keyword of the body names outside code, and an issue linked by hand in the sidebar of the pull request. A
caller adds FIELD to the field list of its `gh pr view`, and `issue` reads that answer. So the rule is GitHub's own,
and no copy of it can drift.
"""

FIELD = "closingIssuesReferences"


def issue(pr, repo):
    """The number of the first issue of `repo` that the pull request closes, or None. An issue of another repo is
    skipped. `pr` is the answer of `gh pr view` with FIELD in its field list."""
    for reference in pr.get(FIELD) or []:
        repository = reference.get("repository") or {}
        name = f"{(repository.get('owner') or {}).get('login')}/{repository.get('name')}"
        if name.casefold() == repo.casefold():
            return reference["number"]
    return None
