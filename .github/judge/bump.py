"""The test of a pure Dependabot bump, for #448: a pull request that the judge passes with no agent session.

`failures` is a pure function of the data that GitHub's REST API returned. It makes no call. Each read fails closed:
a field that is missing or a list that is short is a failed condition, never a pass.

The four conditions:

1. The author of the pull request is `dependabot[bot]`, by `user.login` of `GET /repos/<repo>/pulls/<n>`.
2. The first commit has the author `dependabot[bot]` and `verification.verified` true. Each later commit has the
   author `github-actions[bot]`, the first message line `Regenerate the lock files`, and changes only
   `packages.lock.json` files. The author is the GitHub account, `author.login` of the commit, not its free-text name.
3. Each changed file has the status `modified` and no `previous_filename`. Its path is `Directory.Packages.props` at
   the root, or a `packages.lock.json` file. The list holds `changed_files` of the pull request.
4. Each removed line of the patch of `Directory.Packages.props` pairs in order with one added line. Both lines are
   `<PackageVersion Include="<name>" Version="<version>" />`, with the same name and the same indent. Each version is
   dotted digits, and the first number is the same in both.
"""

import re

DEPENDABOT = "dependabot[bot]"
ACTIONS = "github-actions[bot]"
RELOCK_MESSAGE = "Regenerate the lock files"
PROPS = "Directory.Packages.props"
LOCK = "packages.lock.json"
TITLE = "Dependabot bump, no agent read it"
PACKAGE = re.compile(r'^(\s*)<PackageVersion Include="([^"]+)" Version="(\d+(?:\.\d+)*)" />$')


def login(item):
    return ((item or {}).get("author") or {}).get("login")


def is_lock(path):
    return path == LOCK or path.endswith("/" + LOCK)


def author_failures(pr):
    author = (pr.get("user") or {}).get("login")
    return [] if author == DEPENDABOT else [f"1: the author of the pull request is {author!r}, not {DEPENDABOT}"]


def commit_failures(pr, commits, commit_files):
    """`commits` is the list of `GET /repos/<repo>/pulls/<n>/commits`. `commit_files` maps the SHA of each later commit
    to the `files` of `GET /repos/<repo>/commits/<sha>`."""
    if not commits:
        return ["2: the pull request lists no commit"]
    found = []
    if len(commits) < (pr.get("commits") or 0):
        found.append(f"2: the list of commits holds {len(commits)} of the {pr.get('commits')} of the pull request")
    first = commits[0]
    if login(first) != DEPENDABOT:
        found.append(f"2: the first commit {first.get('sha', '')[:7]} has the author {login(first)!r}")
    if ((first.get("commit") or {}).get("verification") or {}).get("verified") is not True:
        found.append(f"2: the first commit {first.get('sha', '')[:7]} is not verified")
    for commit in commits[1:]:
        sha = commit.get("sha", "")
        if login(commit) != ACTIONS:
            found.append(f"2: the later commit {sha[:7]} has the author {login(commit)!r}, not {ACTIONS}")
        message = ((commit.get("commit") or {}).get("message") or "").split("\n", 1)[0]
        if message != RELOCK_MESSAGE:
            found.append(f"2: the later commit {sha[:7]} has the message {message!r}, not {RELOCK_MESSAGE!r}")
        files = commit_files.get(sha)
        if not files:
            found.append(f"2: the files of the later commit {sha[:7]} are not read")
            continue
        for file in files:
            if not is_lock(file.get("filename") or ""):
                found.append(f"2: the later commit {sha[:7]} changes {file.get('filename')!r}")
    return found


def file_failures(pr, files):
    """`files` is the list of `GET /repos/<repo>/pulls/<n>/files`, every page."""
    found = []
    expected = pr.get("changed_files")
    if not isinstance(expected, int) or len(files) < expected:
        found.append(f"3: the list of files holds {len(files)} of the {expected} of the pull request")
    for file in files:
        path = file.get("filename") or ""
        if file.get("status") != "modified":
            found.append(f"3: {path!r} has the status {file.get('status')!r}, not 'modified'")
        if "previous_filename" in file:
            found.append(f"3: {path!r} was renamed from {file['previous_filename']!r}")
        if path != PROPS and not is_lock(path):
            found.append(f"3: {path!r} is neither {PROPS} nor a {LOCK} file")
    return found


def bumps(files):
    """(the failures of condition 4, the list of (name, old, new)) for the patch of Directory.Packages.props."""
    props = [file for file in files if file.get("filename") == PROPS]
    if not props:
        return [f"4: the pull request does not change {PROPS}"], []
    patch = props[0].get("patch")
    if not patch:
        return [f"4: the patch of {PROPS} is missing"], []
    removed, added = [], []
    for line in patch.split("\n"):
        if line.startswith("-"):
            removed.append(line[1:])
        elif line.startswith("+"):
            added.append(line[1:])
    if not removed or len(removed) != len(added):
        return [f"4: {len(removed)} removed lines and {len(added)} added lines of {PROPS} do not pair"], []
    found, packages = [], []
    for old_line, new_line in zip(removed, added):
        old, new = PACKAGE.match(old_line), PACKAGE.match(new_line)
        if not old or not new:
            found.append(f"4: {old_line.strip()!r} and {new_line.strip()!r} are not two PackageVersion lines")
            continue
        (old_indent, old_name, old_version), (new_indent, new_name, new_version) = old.groups(), new.groups()
        if old_name != new_name or old_indent != new_indent:
            found.append(f"4: {old_name} and {new_name} differ in their name or their indent")
        elif old_version.split(".")[0] != new_version.split(".")[0]:
            found.append(f"4: {old_name} moves from {old_version} to {new_version}, another major version")
        else:
            packages.append((old_name, old_version, new_version))
    return found, packages


def failures(pr, commits, commit_files, files):
    """(each failed condition, each package as (name, old, new)). The pull request is a pure bump when the first list
    is empty."""
    found = author_failures(pr) + commit_failures(pr, commits, commit_files) + file_failures(pr, files)
    found_bumps, packages = bumps(files)
    return found + found_bumps, packages


def summary(packages):
    return "\n".join(f"- {name}: {old} to {new}" for name, old, new in packages)
