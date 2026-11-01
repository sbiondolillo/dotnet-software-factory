---
name: change
description: Route a change to this repo to its builder, write the story the factory builds, or build a direct change in this session. Use when a change to this repo is requested, when a ready issue is built directly, or when a factory run failed on an issue.
disable-model-invocation: true
---

**The factory builds a story by default.** A story is an issue written with the starter's `write-issue` skill, with the labels `ready` and `factory`. `.github/workflows/factory.yml` builds it at `main`, and the owner merges the pull request. The factory sets no assignee and no `building`.

**A direct session builds the rest**, as `## Build` says:

- `factory.yml` and the other workflow files, which the factory cannot test on itself
- a phase prompt
- a prose file: a skill, a template, a page, the README
- a change with no scenario
- a story that a factory run failed on, which the owner sends direct with the run, and with its pull request when it has one. A relaunch of a run that failed before any build, on the runner, the clone or the token, is the owner's `gh run rerun`
- any change the owner sends direct, for any reason. The owner records no justification

A direct change keeps the pull request body as its record. A change on a wayfinder map keeps its ticket as the record. A direct change can get an issue in any form, written by hand or by `write-issue`, and that issue takes the labels below.

| Labels | Meaning |
|---|---|
| `ready` + `factory` | the factory's, and no session touches it until a run of it fails |
| `ready` + `building` | a direct build in progress, claimed by step 1 |
| `ready` + `factory` + `building` | a story the factory failed on, and a session finishes. `factory` stays, as the record of the run |

Start at `## Story` for a request that the factory builds. Start at `## Build` for a direct change. An issue starts at step 1, and a change the owner states in chat starts at step 2. The owner decides between the two, as the list above says, and a request with no decision gets the question.

## Story

Follow `templates/starter/.claude/skills/write-issue/SKILL.md`, with these two readings. Its `docs/write-an-issue.md` is `templates/starter/docs/write-an-issue.md`. Its repo is this one: `main` is DSF's `main`, and the feature files and the step classes are under `specs/features/` and `tests/Specs/Steps/`.

Done when the issue is filed, with its URL in the handover. Then stop. The owner reads the issue and adds `ready`, then `factory`, or comments a change, or closes it. The label `factory` starts the run on its own, so it goes on last. The factory reads the labels, the title and the body, and no comment. So a comment of the owner's goes into the body, by the skill's steps and `gh issue edit <n> --body-file`.

## Build

### 1. Claim the issue and read it

Build an issue that has `ready`. An issue with no `ready` waits for the owner's label. An issue that already has the label `building` belongs to another session: stop, and say so. For an issue with `factory`, read the next paragraph first. Then claim the issue, before any other work.

For a story the factory failed on, the owner names the failed run and its pull request. When the owner names neither, the session asks. The owner is in the chat on the direct path, so the session reads no run list. With an open pull request, `<pr>` is its number, and step 2 continues on its head branch, `factory/<run id>`, where `<run id>` is the id of the run. Otherwise step 2 starts from `origin/main`, and step 4 opens a new pull request. The run's log, `gh run view <run id> --log-failed`, and its comments on the issue say what failed.

```
gh issue edit <n> --add-label building
```

Read the issue and its comments with `gh issue view <n> --comments`. The body is the spec. A comment of the owner's that the body does not hold yet changes the spec.

Done when the spec is in the file `<scratch>/spec`, where `<scratch>` is the session's scratch directory, and matches every comment of the owner's.

### 2. Write the scenarios, then the code

For a change the owner states in chat, the request text in the file `<scratch>/spec`, where `<scratch>` is the session's scratch directory, is the spec.

Work in a worktree of your own, and run every later command there. `<slug>` is a short slug of the issue's title, or of the request for a change stated in chat. The branch depends on the case:

| Case | Command, after `git fetch origin` |
|---|---|
| an issue, or a failed run with no branch | `git worktree add .claude/worktrees/<n>-<slug> -b <n>-<slug> origin/main` |
| a change stated in chat | `git worktree add .claude/worktrees/<slug> -b <slug> origin/main` |
| a failed run with an open pull request | `git worktree add .claude/worktrees/<n>-<slug> factory/<run id>`, which tracks the head branch, so the push of step 4 updates that pull request |

Each block of `## Scenarios` goes into its feature file under `specs/features/`, word for word. The first line of a block, `# specs/features/<name>.feature`, names the file and is not copied, as `IssueScenarios.Apply` does. A block that holds a `Feature:` line replaces its feature file. Any other block edits the file on `main`. A scenario whose name the file holds is replaced whole, and a new name goes at the end. Each name under `## Removed scenarios` is removed from its feature file. Run `dotnet test` and see each new scenario fail. Then write the code until it passes. On the branch of a failed run, the blocks of `## Scenarios` are in their feature files already. Read each against the issue, then fix the code until the checks below pass. A spec that changes behaviour and holds no `## Scenarios`, such as a request in chat, gets its blocks from the session. They follow `templates/starter/docs/write-an-issue.md`. `<scratch>/spec` holds them before the code. A spec with no scenario, such as a prose change, skips to the code. `AGENTS.md` → "What a scenario tests" states that no scenario reads a prose file. A body with `## Scope` changes no path outside it.

Then run every check of the `build` job of `.github/workflows/ci.yml` on the branch:

```
dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
ruff format . && ruff check .
python3 -B -m unittest discover -s .github/repair -p 'test_*.py'
```

`ruff format .` rewraps the Python under `.github/` to `ruff.toml`, and `ruff check .` names each line that it cannot wrap, such as a long docstring or a long string. Wrap each one by hand. The `build` job runs `ruff==0.16.2`, so a check with another version can differ.

A change to code that calls GitHub, `docker` or Claude Code also runs `dotnet run --no-restore --project tests/Integration`, and the result goes in the pull request.

Commit the branch, new files included, so that a reviewer reads every change by one SHA. The commit runs only when `git status --porcelain` prints a line, since `git commit` with no change exits with the code 1. A failed run's pull request that needs only replies keeps its head commit, and step 3 reviews that head. The message, in the file `<scratch>/message`, is the title of the issue, or a Conventional Commits line for a change with no issue:

```
git add -- <each changed path> && git commit -F <scratch>/message
```

A body with `## Check` then runs its block as the factory does, on the tracked files alone. `git worktree add --detach <scratch>/check HEAD`. Then write the block to `<scratch>/check.sh`, run `sh <scratch>/check.sh` in `<scratch>/check`, and run `git worktree remove --force <scratch>/check`. The block exits with the code 0.

Done when every check above passes, and the result of the integration suite is kept when that suite ran. Then `git status --porcelain` lists no path the spec names.

### 3. Review the branch

Run the three angles of [`JUDGES.md`](JUDGES.md) on the commit, as three subagents in parallel, on Opus. The new commit is not pushed, so each reviewer clones this worktree into a scratch directory of its own and checks the SHA out there. Give each one:

- the full SHA of the commit, from `git rev-parse HEAD`
- the full SHA of the base, from `git fetch -q origin && git rev-parse origin/main`
- the path of this worktree
- the path of `<scratch>/spec`, and the issue number when one exists

A spec with no scenario gets `Conformance` and `Regressions and style`, and no `Test strength, by mutation`.

Read each finding against the branch before you act on it. Each finding gets a fix on the branch or a refutation with its evidence. A fix is a new commit, and runs the checks of step 2 again.

A fix that adds behaviour the spec does not name edits the spec in the same turn as the fix. For an issue, the edit goes in its `## Change`, with the date and the review as its source, and `<scratch>/spec` is then written again from the edited body, since the reviewers of this step and the checks of step 4 read that file and never the issue. For a change stated in chat, the edit goes in `<scratch>/spec`, and the report of step 4 carries it. The judge reads the issue at run time, so a run after the edit judges the edited spec. A spec that lags its code gets a rejection for the gap, as PR #380 did.

Done when each angle accepts, or each finding has a fix or a refutation.

### 4. Deliver the branch

For an issue, check the link from the spec to the code. Each scenario name of `<scratch>/spec` is in `specs/features/`, and each name under its `## Removed scenarios` is in no file there.

```
tr -d '\r' < <scratch>/spec | sed -nE 's/^ *Scenario( Outline)?: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" || echo "missing: $name"; done
tr -d '\r' < <scratch>/spec | sed -n '/^## Removed scenarios/,/^## /p' | sed -nE 's/^- `[^`]*`: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" && echo "still present: $name"; done
```

Write the report to a file: what changed, the result of each suite, each reviewer's verdict, and each finding with its fix or its refutation. Then push the branch, `git push -u origin HEAD`. A push with no new commit prints `Everything up-to-date` and starts no check run, so the handover names each red check, and the owner reruns it with `gh run rerun <id> --failed`. For a failed run with an open pull request, each open review thread then gets a reply, as `## Review threads` says: the SHA of its pushed fix, or the evidence that refutes it. A fixed thread is resolved, and a refuted thread stays open for the reviewer, as the factory's feedback path does. Deliver the report by the case:

| Case | Delivery |
|---|---|
| an issue, or a failed run with no branch | `gh pr create --title '<the issue title>' --body-file <report>`, with the line `Closes #<n>` in the report, or `Part of #<n>` for an issue with a check after the merge |
| a change stated in chat | `gh pr create --title '<the commit title>' --body-file <report>` |
| a failed run with an open pull request | `gh pr comment <pr> --body-file <report>`. The push put the fix on the pull request |

An issue with a check after the merge is one whose `## Done when` holds an item that only a run after the merge shows, such as a dispatch of a workflow or the title of a run. GitHub closes an issue that the body or a commit message of a merged pull request names with `Closes #<n>`, so neither one names it that way, and the issue stays open for those checks. The line `Part of #<n>` stands alone, and the judge, `.github/judge/judge.py`, reads the issue it names as the spec. The handover lists each such item, and the owner closes the issue once each one holds.

A fix after the push, from the judge or from a Copilot review, is a new commit under step 3: the checks of step 2 run again, and a fix that adds behaviour edits the spec in the same turn.

Done when the check above prints nothing for an issue, the report is on the pull request, and its checks are green. For a failed run, each fixed thread holds the SHA of its fix and is resolved, and each open thread holds a refutation. The owner merges it, and removes the claim of an issue or a failed run with `gh issue edit <n> --remove-label building`. The handover gives that command with the merge commands. The worktree stays for a fix the owner asks for, and the owner removes it before the merge. `gh pr merge --delete-branch` cannot delete a branch that a worktree holds.

## Review threads

The open review threads of a pull request, for a failed run:

```
gh api graphql -f query='query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){reviewThreads(first:100){nodes{id isResolved path line comments(first:100){nodes{author{login} body}}}}}}}' -F o='{owner}' -F r='{repo}' -F n=<pr>
```

Each thread with `isResolved` false gets a reply with a character other than whitespace, as `AGENTS.md` → "GitHub" states. The reply is in the file `<scratch>/reply`:

```
gh api graphql -f query='mutation($t:ID!,$b:String!){addPullRequestReviewThreadReply(input:{pullRequestReviewThreadId:$t,body:$b}){comment{id}}}' -f t=<id> -F b=@<scratch>/reply
```

A thread whose fix is pushed is then resolved:

```
gh api graphql -f query='mutation($t:ID!){resolveReviewThread(input:{threadId:$t}){thread{isResolved}}}' -f t=<id>
```
