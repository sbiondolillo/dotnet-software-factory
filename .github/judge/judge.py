#!/usr/bin/env python3
"""The judge: one Managed Agents session per angle of .claude/skills/change/JUDGES.md, on one pull request.

The workflow job is the orchestrator. It holds the Anthropic API key and the GitHub token, starts one
session per angle with a read-only repository token, computes the verdict in code, starts one synthesis
session only when an angle rejects, and posts the review and a check run with its own token. The sessions
hold the read-only token and nothing else.

Environment: JUDGE_ANTHROPIC_API_KEY, JUDGE_GITHUB_RO_TOKEN, GITHUB_TOKEN, GITHUB_REPOSITORY,
JUDGE_AGENT_ID, JUDGE_ENVIRONMENT_ID, JUDGE_ANGLES (comma-separated, default "Conformance,Regressions and style"),
JUDGE_BUDGET_CENTS (default 500), JUDGE_TIMEOUT_SECONDS (default 1800).
Argument: the pull request number.
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
import urllib.error

API = "https://api.anthropic.com"
BETA = "managed-agents-2026-04-01"
MARKER = "<!-- judge:{sha}:{spec} -->"


def env(name, default=None):
    value = os.environ.get(name, default)
    if value is None or value == "":
        sys.exit(f"judge: {name} is not set")
    return value


def anthropic(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(API + path, data=data, method=method, headers={
        "x-api-key": env("JUDGE_ANTHROPIC_API_KEY"), "anthropic-version": "2023-06-01",
        "anthropic-beta": BETA, "content-type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        sys.exit(f"judge: {method} {path} -> {error.code}: {error.read().decode()[:500]}")


def gh(*args, input_text=None):
    result = subprocess.run(["gh", *args], capture_output=True, text=True, input=input_text)
    if result.returncode != 0:
        sys.exit(f"judge: gh {' '.join(args[:3])} failed: {result.stderr.strip()[:500]}")
    return result.stdout


def pull_request(repo, number):
    fields = "number,headRefOid,baseRefOid,baseRefName,headRefName,body,title,url"
    return json.loads(gh("pr", "view", str(number), "-R", repo, "--json", fields))


def spec_for(repo, pr):
    """The spec is the body of the issue the pull request closes, or the pull request body."""
    match = re.search(r"(?i)\b(?:closes|fixes|resolves)\s+#(\d+)", pr["body"] or "")
    if match:
        issue = json.loads(gh("issue", "view", match.group(1), "-R", repo, "--json", "number,title,body"))
        return f"# {issue['title']} (#{issue['number']})\n\n{issue['body']}", issue["number"]
    return f"# {pr['title']} (pull request #{pr['number']})\n\n{pr['body'] or ''}", None


def spec_digest(spec):
    """The first 12 hex digits of the spec's SHA-256, so an edited spec on the same head is judged again."""
    return hashlib.sha256(spec.encode()).hexdigest()[:12]


def already_judged(repo, number, sha, spec):
    comments = json.loads(gh("api", f"repos/{repo}/issues/{number}/comments", "--paginate"))
    return any(MARKER.format(sha=sha, spec=spec_digest(spec)) in (c.get("body") or "") for c in comments)


def reviewer_prompt(angle, repo, head, base, spec):
    return f"""You are one reviewer of a pull request of the repository {repo}, mounted at /workspace/{repo.split('/')[1]}.

Read /workspace/{repo.split('/')[1]}/.claude/skills/change/JUDGES.md first: its preamble, then the section "## {angle}". Run that angle only, with every command it names, in the mounted checkout. The checkout is your own, so skip the clone step: the mounted clone is shallow and at the default branch, so first run, in the mounted directory:

git fetch --depth=400 origin {head} {base} && git checkout -q {head}

Then `<clone>` is the mounted directory, `<SHA>` is {head}, `<base>` is {base}, and `<fork>` is `git merge-base {base} {head}`. The spec is the text under "## Spec" below; write it to /tmp/spec.md first and treat that file as the spec file the angle names. Edit no tracked file, and post nothing.

Your final answer is one JSON object and nothing else, with no prose before or after it and no code fence:
{{"angle": "{angle}", "verdict": "accept" or "reject", "findings": [{{"file": "...", "line": 0, "fault": "one sentence", "evidence": "the command and its output", "fix": "...", "nitpick": false}}], "clean": ["one line per thing found clean"]}}
A finding with no evidence is not a finding. A nitpick alone does not make the verdict "reject".

## Spec

{spec}
"""


def start_session(agent, environment, title, prompt, metadata, budget_cents, resources=None, overrides=None):
    body = {
        "agent": {"type": "agent_with_overrides", "id": agent, **overrides} if overrides else agent,
        "environment_id": environment, "title": title, "metadata": metadata,
        "budget": {"type": "limit", "max_list_cost": {"amount": str(budget_cents), "currency": "USD"}},
        "initial_events": [{"type": "user.message", "content": [{"type": "text", "text": prompt}]}],
    }
    if resources:
        body["resources"] = resources
    return anthropic("POST", "/v1/sessions", body)["id"]


def wait_idle(session_ids, timeout):
    deadline = time.time() + timeout
    pending = set(session_ids)
    while pending and time.time() < deadline:
        for sid in list(pending):
            status = anthropic("GET", f"/v1/sessions/{sid}")["status"]
            if status in ("idle", "terminated", "error"):
                pending.discard(sid)
        if pending:
            time.sleep(15)
    if pending:
        sys.exit(f"judge: sessions still running after {timeout} s: {sorted(pending)}")


def final_message(sid):
    events = anthropic("GET", f"/v1/sessions/{sid}/events?limit=200")["data"]
    texts = [c["text"] for e in events if e.get("type") == "agent.message"
             for c in e.get("content", []) if c.get("type") == "text"]
    stop = [e.get("stop_details") for e in events if e.get("type") == "session.status_idle"]
    usage = [e.get("usage") for e in events if e.get("type") == "session.usage"]
    return (texts[-1] if texts else ""), (stop[-1] if stop else None), (usage[-1] if usage else {})


def parse_report(angle, text):
    """The reviewer's JSON, or a reject that names the parse fault."""
    candidate = text.strip()
    match = re.search(r"\{.*\}", candidate, re.S)
    try:
        report = json.loads(match.group(0) if match else candidate)
        assert report.get("verdict") in ("accept", "reject")
        report.setdefault("findings", [])
        report.setdefault("clean", [])
        report["angle"] = angle
        return report
    except (ValueError, AssertionError):
        return {"angle": angle, "verdict": "reject", "findings": [{
            "file": "", "line": 0, "fault": "the reviewer's final message is not the JSON object the contract names",
            "evidence": candidate[:800], "fix": "rerun the angle", "nitpick": False}], "clean": []}


def render(reports, synthesis, pr, costs, spec):
    verdict = "CHANGES REQUESTED" if any(r["verdict"] == "reject" for r in reports) else "APPROVED"
    lines = [MARKER.format(sha=pr["headRefOid"], spec=spec_digest(spec)), f"**{verdict}** by the judge on `{pr['headRefOid'][:7]}`.", ""]
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
    return verdict, "\n".join(lines)


def main():
    repo = env("GITHUB_REPOSITORY")
    number = int(sys.argv[1])
    agent = env("JUDGE_AGENT_ID")
    environment = env("JUDGE_ENVIRONMENT_ID")
    angles = [a.strip() for a in os.environ.get("JUDGE_ANGLES", "Conformance,Regressions and style").split(",") if a.strip()]
    budget = int(os.environ.get("JUDGE_BUDGET_CENTS", "500"))
    timeout = int(os.environ.get("JUDGE_TIMEOUT_SECONDS", "1800"))

    pr = pull_request(repo, number)
    head, base = pr["headRefOid"], pr["baseRefOid"]
    spec, issue = spec_for(repo, pr)
    if already_judged(repo, number, head, spec):
        print(f"judge: {head[:7]} with this spec is already judged; nothing to do")
        return
    print(f"judge: pull request #{number} {head[:7]} on {base[:7]}, spec from " + (f"issue #{issue}" if issue else "the pull request body"))

    resources = [{"type": "github_repository", "url": f"https://github.com/{repo}",
                  "mount_path": f"/workspace/{repo.split('/')[1]}", "authorization_token": env("JUDGE_GITHUB_RO_TOKEN")}]
    sessions = {}
    for angle in angles:
        sid = start_session(agent, environment, f"judge {repo}#{number} {angle}", reviewer_prompt(angle, repo, head, base, spec),
                            {"repo": repo, "pr": str(number), "sha": head, "angle": angle}, budget, resources)
        sessions[angle] = sid
        print(f"judge: started {angle}: {sid}")
    wait_idle(sessions.values(), timeout)

    reports, costs = [], []
    for angle, sid in sessions.items():
        text, stop, usage = final_message(sid)
        cost = (usage.get("list_cost") or {}).get("amount", "?")
        costs.append((sid, angle, cost))
        if stop and stop.get("type") == "refusal":
            reports.append({"angle": angle, "verdict": "reject", "findings": [{
                "file": "", "line": 0, "fault": "the session was refused before any command ran",
                "evidence": json.dumps(stop), "fix": "rerun the angle", "nitpick": False}], "clean": []})
        else:
            reports.append(parse_report(angle, text))
        print(f"judge: {angle} -> {reports[-1]['verdict']}, {cost} cents")

    synthesis = None
    if any(r["verdict"] == "reject" for r in reports):
        prompt = ("Three to one reviewers of one pull request reported the JSON objects below. Write the review body for the pull request: "
                  "the findings ordered by severity, duplicates merged, every finding kept, each with its file and line, fault, evidence and fix, "
                  "then the clean items in one short list. Plain Markdown, no heading above level 3, no preamble.\n\n"
                  + "\n\n".join(json.dumps(r, indent=1) for r in reports))
        sid = start_session(agent, environment, f"judge {repo}#{number} synthesis", prompt,
                            {"repo": repo, "pr": str(number), "sha": head, "angle": "synthesis"}, 100,
                            overrides={"tools": [{"type": "agent_toolset_20260401", "default_config": {"enabled": False}}]})
        wait_idle([sid], 600)
        synthesis, _, usage = final_message(sid)
        costs.append((sid, "synthesis", (usage.get("list_cost") or {}).get("amount", "?")))

    verdict, body = render(reports, synthesis, pr, costs, spec)
    gh("pr", "comment", str(number), "-R", repo, "--body-file", "-", input_text=body)
    conclusion = "success" if verdict == "APPROVED" else "failure"
    gh("api", f"repos/{repo}/check-runs", "-f", "name=judge-verdict", "-f", f"head_sha={head}", "-f", "status=completed",
       "-f", f"conclusion={conclusion}", "-f", f"output[title]={verdict}", "-f",
       f"output[summary]={len(reports)} angles, {sum(len(r['findings']) for r in reports)} findings")
    print(f"judge: {verdict}")


if __name__ == "__main__":
    main()
