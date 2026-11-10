#!/usr/bin/env python3
"""The judge: one Managed Agents session per angle of .claude/skills/change/JUDGES.md, on one pull request.

The workflow job is the orchestrator. It holds the Anthropic API key and the GitHub token, starts one
session per angle with a read-only repository token, computes the verdict in code, starts one synthesis
session only when an angle rejects, and posts the review and a check run with its own token. The sessions
hold the read-only token and nothing else. Each session is a Session of sessions.py in .github/shared/, over the one
adapter of anthropic.py there. A reviewer's Report that fails the parse gets one correction turn, and a session that
gives no Report is a reject of its angle, with the reason as the fault.

Environment: JUDGE_ANTHROPIC_API_KEY, JUDGE_GITHUB_RO_TOKEN, GITHUB_TOKEN, GITHUB_REPOSITORY,
JUDGE_AGENT_ID, JUDGE_ENVIRONMENT_ID, JUDGE_ANGLES (comma-separated, default "Conformance,Regressions and style"),
JUDGE_BUDGET_CENTS (default 500), JUDGE_TIMEOUT_SECONDS (default 1800).
Argument: the pull request number.

A pull request that Dependabot opened takes the automatic path on the event `pull_request`, for #448: bump.py tests
the four conditions of a pure bump, and a pure bump gets judge-verdict as success with no session. That path reads
no JUDGE_* variable, since a run with Dependabot as its actor holds no JUDGE_* secret. It posts no comment and no
marker, so a later dispatch by hand is not deduped away. A bump that fails a condition gets no check run: the job
prints each failed condition and exits with the code 0. `workflow_dispatch` always runs the full judge.
"""

import hashlib
import json
import os
import re
import sys
import time

import bump

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shared"))
import closing  # noqa: E402
import sessions  # noqa: E402
import verdict  # noqa: E402
from anthropic import Anthropic, AnthropicError  # noqa: E402
from github import GitHub  # noqa: E402
from sessions import Session, SessionError  # noqa: E402

MARKER = "<!-- judge:{sha}:{spec} -->"


def env(name, default=None):
    value = os.environ.get(name, default)
    if value is None or value == "":
        sys.exit(f"judge: {name} is not set")
    return value


def pull_request(gh, repo, number):
    fields = f"number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url,{closing.FIELD}"
    return json.loads(gh.run("pr", "view", str(number), "-R", repo, "--json", fields))


def spec_for(gh, repo, pr):
    """The spec is the body of the issue the pull request closes, or of the issue a line `Part of #<n>` names, or the
    pull request body. GitHub reports the issue the pull request closes, as closing.py reads it. A pull request names
    its issue with `Part of #<n>` when a check after the merge keeps it open. Code in the body names no issue, so a
    quote of a `Part of #<n>` in backticks names none."""
    number = closing.issue(pr, repo)
    if number is None:
        body = re.sub(r"`[^`\n]*`", "", re.sub(r"```.*?```", "", pr["body"] or "", flags=re.S))
        match = re.search(r"^Part of #(\d+)\s*$", body, re.M)
        number = int(match.group(1)) if match else None
    if number is not None:
        issue = json.loads(gh.run("issue", "view", str(number), "-R", repo, "--json", "number,title,body"))
        return f"# {issue['title']} (#{issue['number']})\n\n{issue['body']}", issue["number"]
    return f"# {pr['title']} (pull request #{pr['number']})\n\n{pr['body'] or ''}", None


def spec_digest(spec):
    """The first 12 hex digits of the spec's SHA-256, so an edited spec on the same head is judged again."""
    return hashlib.sha256(spec.encode()).hexdigest()[:12]


def already_judged(gh, repo, number, sha, spec):
    comments = gh.api_list(f"repos/{repo}/issues/{number}/comments")
    return any(MARKER.format(sha=sha, spec=spec_digest(spec)) in (c.get("body") or "") for c in comments)


REPORT = """Your final answer is one JSON object and nothing else, with no prose before or after it and no code fence:
{{"angle": "{angle}", "verdict": "accept" or "reject",
 "findings": [{{"file": "...", "line": 0, "fault": "one sentence", "evidence": "the command and its output",
               "fix": "...", "nitpick": false}}],
 "clean": ["one line per thing found clean"]}}"""


def reviewer_prompt(angle, repo, head, base, spec):
    mount = f"/workspace/{repo.split('/')[1]}"
    return f"""You are one reviewer of a pull request of the repository {repo}, mounted at {mount}.

Read {mount}/.claude/skills/change/JUDGES.md first: its preamble, then the section "## {angle}". Run that angle
only, with every command it names, in the mounted checkout. The checkout is your own, so skip the clone step: the
mounted clone is shallow and at the default branch, so first run, in the mounted directory:

git fetch --depth=400 origin {head} {base} && git checkout -q {head}

Then `<clone>` is the mounted directory, `<SHA>` is {head}, `<base>` is {base}, and `<fork>` is
`git merge-base {base} {head}`. The spec is the text under "## Spec" below; write it to /tmp/spec.md first and treat
that file as the spec file the angle names. Edit no tracked file, and post nothing.

{REPORT.format(angle=angle)}
A finding with no evidence is not a finding. A nitpick alone does not make the verdict "reject".

## Spec

{spec}
"""


def reject(angle, fault, evidence):
    """A reject report of an angle with one finding, for a reviewer that gave no Report."""
    return {
        "angle": angle,
        "verdict": "reject",
        "findings": [
            {
                "file": "",
                "line": 0,
                "fault": fault,
                "evidence": evidence,
                "fix": "rerun the angle",
                "nitpick": False,
            }
        ],
        "clean": [],
    }


def parse_report(angle, text):
    """The reviewer's Report, or ValueError with the fault."""
    try:
        report = sessions.json_object(text)
    except ValueError as error:
        raise ValueError(f"{type(error).__name__}: {error}") from error
    if not isinstance(report, dict):
        raise ValueError(f"the Report is a JSON {type(report).__name__}, not an object")
    if report.get("verdict") not in ("accept", "reject"):
        raise ValueError(f"the verdict {report.get('verdict')!r} is not one of accept, reject")
    report.setdefault("findings", [])
    report.setdefault("clean", [])
    report["angle"] = angle
    return report


def cost(usage):
    return (usage.get("list_cost") or {}).get("amount", "?")


def read_angles(started, deadline):
    """The report and the cost of each angle, in the order the angles started. A session that gives no Report is a
    reject of its angle, with the reason as the fault and the last message as the evidence, and the other angles are
    still read."""
    reports, costs = [], []
    for angle, session in started.items():
        try:
            answer = session.report(
                lambda text, angle=angle: parse_report(angle, text), REPORT.format(angle=angle), deadline
            )
            reports.append(answer.value)
            costs.append((session.sid, angle, cost(answer.usage)))
        except SessionError as error:
            reports.append(reject(angle, error.reason, error.text[:800]))
            costs.append((session.sid, angle, "?"))
        print(f"judge: {angle} -> {reports[-1]['verdict']}, {costs[-1][2]} cents")
    return reports, costs


def read_synthesis(session, deadline):
    """The review text of the synthesis and its cost. The text is Markdown, so its parse keeps the text as it is and
    never asks for a correction. The verdict comes from the reports alone, so a synthesis that gives no text, such as
    one refused or past the deadline, gives its last message, or None, and the review is still posted. An empty text
    is None too."""
    try:
        answer = session.report(lambda text: text, "", deadline)
    except SessionError as error:
        print(f"judge: the synthesis gave no text: {error.reason}")
        return error.text or None, "?"
    return answer.value or None, cost(answer.usage)


def render(reports, synthesis, pr, costs, spec):
    outcome = "CHANGES REQUESTED" if any(r["verdict"] == "reject" for r in reports) else "APPROVED"
    lines = [
        MARKER.format(sha=pr["headRefOid"], spec=spec_digest(spec)),
        f"**{outcome}** by the judge on `{pr['headRefOid'][:7]}`.",
        "",
    ]
    if synthesis:
        lines += [synthesis.strip(), ""]
    for r in reports:
        lines.append(f"### {r['angle']}: {r['verdict']}")
        for f in r["findings"]:
            tag = " (nitpick)" if f.get("nitpick") else ""
            where = f"`{f.get('file')}:{f.get('line')}` " if f.get("file") else ""
            lines.append(f"- {where}{f.get('fault')}{tag}")
            if f.get("evidence"):
                lines.append(f"  ```\n  {str(f['evidence']).strip()[:1500]}\n  ```")
            if f.get("fix"):
                lines.append(f"  Fix: {f['fix']}")
        for c in r["clean"]:
            lines.append(f"- clean: {c}")
        lines.append("")
    lines.append("Sessions: " + ", ".join(f"`{sid}` {angle} {cost} cents" for sid, angle, cost in costs))
    return outcome, "\n".join(lines)


def takes_bump_path(event, author):
    """True for the automatic path: the event `pull_request` on a pull request that Dependabot opened."""
    return event == "pull_request" and author == bump.DEPENDABOT


def judge_bump(gh, repo, number, pr):
    """The automatic path for a Dependabot pull request: judge-verdict as success on a pure bump, nothing otherwise."""
    head = pr["head"]["sha"]
    commits = gh.api_list(f"repos/{repo}/pulls/{number}/commits")
    details = {c["sha"]: json.loads(gh.run("api", f"repos/{repo}/commits/{c['sha']}")) for c in commits[1:]}
    files = gh.api_list(f"repos/{repo}/pulls/{number}/files")
    after = json.loads(gh.run("api", f"repos/{repo}/pulls/{number}"))
    if after["head"]["sha"] != head:
        print(f"judge: the head of pull request #{number} moved off {head[:7]} while the job read it; nothing posted")
        return
    failed, packages = bump.failures(after, commits, details, files)
    if failed:
        print(f"judge: pull request #{number} {head[:7]} is not a pure bump; no check run. Dispatch the judge by hand.")
        for line in failed:
            print(f"judge: failed {line}")
        return
    verdict.post(gh, repo, head, "success", bump.TITLE, bump.summary(packages))
    print(f"judge: {bump.TITLE}, {len(packages)} packages on {head[:7]}")


def main(gh=None):
    gh = gh if gh is not None else GitHub.from_env()
    repo = env("GITHUB_REPOSITORY")
    number = int(sys.argv[1])
    event = os.environ.get("GITHUB_EVENT_NAME")
    if event == "pull_request":
        rest = json.loads(gh.run("api", f"repos/{repo}/pulls/{number}"))
        if takes_bump_path(event, (rest.get("user") or {}).get("login")):
            judge_bump(gh, repo, number, rest)
            return
    agent = env("JUDGE_AGENT_ID")
    environment = env("JUDGE_ENVIRONMENT_ID")
    angles = [
        a.strip() for a in os.environ.get("JUDGE_ANGLES", "Conformance,Regressions and style").split(",") if a.strip()
    ]
    budget = int(os.environ.get("JUDGE_BUDGET_CENTS", "500"))
    timeout = int(os.environ.get("JUDGE_TIMEOUT_SECONDS", "1800"))

    pr = pull_request(gh, repo, number)
    head, base = pr["headRefOid"], pr["baseRefOid"]
    spec, issue = spec_for(gh, repo, pr)
    if already_judged(gh, repo, number, head, spec):
        print(f"judge: {head[:7]} with this spec is already judged; nothing to do")
        return
    print(
        f"judge: pull request #{number} {head[:7]} on {base[:7]}, spec from "
        + (f"issue #{issue}" if issue else "the pull request body")
    )

    api = Anthropic.from_env()
    resources = [sessions.repository(repo, env("JUDGE_GITHUB_RO_TOKEN"))]
    started = {}
    for angle in angles:
        session = Session.start(
            api,
            agent,
            environment,
            f"judge {repo}#{number} {angle}",
            reviewer_prompt(angle, repo, head, base, spec),
            {"repo": repo, "pr": str(number), "sha": head, "angle": angle},
            budget,
            resources,
        )
        started[angle] = session
        print(f"judge: started {angle}: {session.sid}")
    reports, costs = read_angles(started, time.time() + timeout)

    synthesis = None
    if any(r["verdict"] == "reject" for r in reports):
        prompt = (
            "Three to one reviewers of one pull request reported the JSON objects below. "
            "Write the review body for the pull request: the findings ordered by severity, duplicates merged, "
            "every finding kept, each with its file and line, fault, evidence and fix, "
            "then the clean items in one short list. Plain Markdown, no heading above level 3, no preamble.\n\n"
            + "\n\n".join(json.dumps(r, indent=1) for r in reports)
        )
        session = Session.start(
            api,
            agent,
            environment,
            f"judge {repo}#{number} synthesis",
            prompt,
            {"repo": repo, "pr": str(number), "sha": head, "angle": "synthesis"},
            100,
            overrides={"tools": [{"type": "agent_toolset_20260401", "default_config": {"enabled": False}}]},
        )
        synthesis, spent = read_synthesis(session, time.time() + 600)
        costs.append((session.sid, "synthesis", spent))

    outcome, body = render(reports, synthesis, pr, costs, spec)
    gh.run("pr", "comment", str(number), "-R", repo, "--body-file", "-", input_text=body)
    conclusion = "success" if outcome == "APPROVED" else "failure"
    findings = sum(len(r["findings"]) for r in reports)
    verdict.post(gh, repo, head, conclusion, outcome, f"{len(reports)} angles, {findings} findings")
    print(f"judge: {outcome}")


if __name__ == "__main__":
    try:
        main()
    except AnthropicError as error:
        sys.exit(f"judge: {error}")
