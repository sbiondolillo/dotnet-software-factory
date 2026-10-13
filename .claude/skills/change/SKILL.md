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
- a story that a factory run failed on after the build started. The Redos ran out, or the build or the review stopped on an error. Or the run delivered a pull request whose checks stayed red or whose review threads stayed open. A run that failed before the build, on the runner, the clone or the token, gets one relaunch instead. The owner routes a run that refused the issue: a fix to the body and a relaunch, or a direct build
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

Build an issue that has `ready`. An issue with no `ready` waits for the owner's label. An issue that already has the label `building` belongs to another session: stop, and say so. For an issue with `factory`, first find its run and its pull request, as the next two paragraphs say, and stop when either says so. Then claim the issue, before any other work.

```
gh issue edit <n> --add-label building
```

**The run of an issue with `factory`.** List the runs: `gh run list --workflow factory.yml -L 200 --json databaseId,displayTitle,event,status,conclusion,attempt,createdAt`. Each label on the issue starts a run, and a run for a label other than `factory` is `skipped`. Its run is the newest whose `conclusion` is not `skipped` and whose log holds the issue's number: `gh run view <databaseId> --log | grep -Ew -- '--issue <n>'`. Runs created before the issue are not its runs. A title is not the key, since a title can change and two issues can share one. A run whose `status` is not `completed` has no log yet, and `gh run view --log` fails for it. When such a run has the issue's title or `factory` as its `displayTitle`, stop, and say so. A run from the label has the `displayTitle` of the issue, and a run by hand has the `displayTitle` `factory`. Then read its failed log, `gh run view <databaseId> --log-failed`, and act on the run:

| The run | Action |
|---|---|
| `status` is not `completed` | the factory's: stop, and say so |
| `conclusion` is `success` | it delivered a green pull request: stop, and say so |
| `conclusion` is `failure` with no line `build: start` in the log, and `attempt` is 1 | no build ran: relaunch it once, `gh run rerun <databaseId>`, and stop, unless the owner sent it direct |
| `conclusion` is `failure` with no line `build: start` in the log, and `attempt` is 2 or more | the relaunch failed too: stop, and give the owner the log line, unless the owner sent it direct |
| `conclusion` is `failure`, with a line `build: start` in the log | a failed run: read its comments on the issue, and go on |
| any other conclusion | stop, and say so |

A failed run that delivered a pull request commented `Opened <url>` on the issue, and `gh pr view <url> --json number,headRefName,state` gives `<pr>` and the branch `factory/<run id>`. When `state` is `OPEN`, step 2 continues on that branch. When `state` is `MERGED`, stop, and say so. When `state` is `CLOSED`, the owner closed it. Step 2 then starts from `origin/main`, as for a run with no branch, and step 4 opens a new pull request. A run with no such comment pushed no branch, so step 2 starts from `origin/main`.

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

Each block of `## Scenarios` goes into its feature file under `specs/features/`, word for word. A block that holds a `Feature:` line replaces its feature file. Any other block edits the file on `main`. A scenario whose name the file holds is replaced whole, and a new name goes at the end. Each name under `## Removed scenarios` is removed from its feature file. Run `dotnet test` and see each new scenario fail. Then write the code until it passes. On the branch of a failed run, the blocks of `## Scenarios` are in their feature files already. Read each against the issue, then fix the code until the checks below pass. A spec that changes behaviour and holds no `## Scenarios`, such as a request in chat, gets its blocks from the session. They follow `templates/starter/docs/write-an-issue.md`. `<scratch>/spec` holds them before the code. A spec with no scenario, such as a prose change, skips to the code. `AGENTS.md` → "What a scenario tests" states that no scenario reads a prose file. A body with `## Scope` changes no path outside it.

Then run every check of the `build` job of `.github/workflows/ci.yml` on the branch:

```
dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
```

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

Done when each angle accepts, or each finding has a fix or a refutation.

### 4. Deliver the branch

For an issue, check the link from the spec to the code. Each scenario name of `<scratch>/spec` is in `specs/features/`, and each name under its `## Removed scenarios` is in no file there.

```
tr -d '\r' < <scratch>/spec | sed -nE 's/^ *Scenario( Outline)?: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" || echo "missing: $name"; done
tr -d '\r' < <scratch>/spec | sed -n '/^## Removed scenarios/,/^## /p' | sed -nE 's/^- `[^`]*`: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" && echo "still present: $name"; done
```

Write the report to a file: what changed, the result of each suite, each reviewer's verdict, and each finding with its fix or its refutation. Then push the branch, `git push -u origin HEAD`. For a failed run with an open pull request, each open review thread then gets a reply, as `## Review threads` says: the SHA of its pushed fix, or the evidence that refutes it. Deliver the report by the case:

| Case | Delivery |
|---|---|
| an issue, or a failed run with no branch | `gh pr create --title '<the issue title>' --body-file <report>`, with the line `Closes #<n>` in the report |
| a change stated in chat | `gh pr create --title '<the commit title>' --body-file <report>` |
| a failed run with an open pull request | `gh pr comment <pr> --body-file <report>`. The push put the fix on the pull request |

Done when the check above prints nothing for an issue, the report is on the pull request, and its checks are green. For a failed run, the pull request also has no open review thread that lacks a reply. The owner merges it, and removes the claim of an issue or a failed run with `gh issue edit <n> --remove-label building`. The handover gives that command with the merge commands. The worktree stays for a fix the owner asks for, and the owner removes it before the merge. `gh pr merge --delete-branch` cannot delete a branch that a worktree holds.

## Review threads

The open review threads of a pull request, for a failed run:

```
gh api graphql -f query='query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){reviewThreads(first:100){nodes{id isResolved path line comments(first:100){nodes{author{login} body}}}}}}}' -F o='{owner}' -F r='{repo}' -F n=<pr>
```

Each thread with `isResolved` false gets a reply with a character other than whitespace, as `AGENTS.md` → "GitHub" states. The reply is in the file `<scratch>/reply`:

```
gh api graphql -f query='mutation($t:ID!,$b:String!){addPullRequestReviewThreadReply(input:{pullRequestReviewThreadId:$t,body:$b}){comment{id}}}' -f t=<id> -F b=@<scratch>/reply
```
