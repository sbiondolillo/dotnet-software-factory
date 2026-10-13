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
- a story that a factory run failed on in the build: the gates stayed red after the Redos. A run that failed before the build, on the runner, the clone or the token, gets one relaunch instead
- any change the owner sends direct, for any reason. The owner records no justification

A direct change keeps the pull request body as its record. A change on a wayfinder map keeps its ticket as the record. A direct change may get an issue in any form, written by hand or by `write-issue`, and that issue takes the labels below.

| Labels | Meaning |
|---|---|
| `ready` + `factory` | the factory's, and no session touches it |
| `ready` + `building` | a direct build in progress, claimed by step 1 |
| `ready` + `factory` + `building` | a story the factory failed on, and a session finishes. `factory` stays, as the record of the run |

Start at `## Story` for a request that the factory builds. Start at `## Build` for a direct change. An issue starts at step 1, and a change the owner states in chat starts at step 2. The owner decides between the two, as the list above says, and a request with no decision gets the question.

## Story

Follow `templates/starter/.claude/skills/write-issue/SKILL.md`, with these two readings. Its `docs/write-an-issue.md` is `templates/starter/docs/write-an-issue.md`. Its repo is this one: `main` is DSF's `main`, and the feature files and the step classes are under `specs/features/` and `tests/Specs/Steps/`.

Done when the issue is filed, with its URL in the handover. Then stop. The owner reads the issue and adds `ready`, then `factory`, or comments a change, or closes it. The label `factory` starts the run on its own, so it goes on last. The factory reads the body alone. So a comment of the owner's goes into the body, by the skill's steps and `gh issue edit <n> --body-file`.

## Build

### 1. Claim the issue and read it

Claim the issue before any other work. An issue that already has the label `building` belongs to another session: stop, and say so.

```
gh issue edit <n> --add-label building
```

Read the issue and its comments with `gh issue view <n> --comments`. The body is the spec. A comment of the owner's that the body does not hold yet changes the spec. For a story the factory failed on, read the run's comment on the issue and its log too, `gh run view <id> --log-failed`. A run whose Redos ran out pushed no branch, so step 2 starts from `origin/main`. A run that delivered a pull request whose checks stayed red left its head branch `factory/<run id>` on origin, and step 2 continues on it.

Done when the spec is in one file in the session's scratch directory, and matches every comment of the owner's.

### 2. Write the scenarios, then the code

For a change the owner states in chat, the request text in a file in the scratch directory is the spec.

Work in a worktree of your own, and run every later command there. The branch depends on the case:

| Case | Command, after `git fetch origin` |
|---|---|
| an issue | `git worktree add .claude/worktrees/<n>-<a short slug> -b <n>-<a short slug> origin/main` |
| a change stated in chat | `git worktree add .claude/worktrees/<a short slug> -b <a short slug> origin/main` |
| a failed run with an open pull request | `git worktree add .claude/worktrees/<n>-<a short slug> factory/<run id>`, which tracks the head branch, so the push of step 4 updates that pull request |

Each block of `## Scenarios` goes into its feature file under `specs/features/`, word for word. A block that holds a `Feature:` line replaces its feature file. Any other block edits the file on `main`: a scenario whose name the file holds is replaced whole, and a new name goes at the end. Each name under `## Removed scenarios` is removed from its feature file. Run `dotnet test` and see each new scenario fail. Then write the code until it passes. A spec with no scenario, such as a prose change, skips to the code. `AGENTS.md` → "What a scenario tests" states that no scenario reads a prose file. A body with `## Scope` changes no path outside it. A body with `## Check` runs its block with `sh -c` in the work tree, and the block exits with the code 0.

Then run every check of the `build` job of `.github/workflows/ci.yml` on the branch:

```
dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
```

A change to code that calls GitHub, `docker` or Claude Code also runs `dotnet run --no-restore --project tests/Integration`, and the result goes in the pull request.

Commit the branch, new files included, so that a reviewer reads every change by one SHA. The message is the title of the issue, or a Conventional Commits line for a change with no issue:

```
git add -- <each changed path> && git commit -m '<title>'
```

Done when every check above passes, the result of the integration suite is kept when that suite ran, and `git status --porcelain` lists no path the spec names.

### 3. Review the branch

Run the three angles of [`JUDGES.md`](JUDGES.md) on the commit, as three subagents in parallel, on Opus. The branch is not pushed, so each reviewer clones this worktree into a scratch directory of its own and checks the SHA out there. Give each one:

- the full SHA of the commit, from `git rev-parse HEAD`
- the full SHA of the base, from `git fetch -q origin && git rev-parse origin/main`
- the path of this worktree
- the path of the spec file, and the issue number when one exists

A spec with no scenario gets `Conformance` and `Regressions and style`, and no `Test strength`.

Read each finding against the branch before you act on it. Each finding gets a fix on the branch or a refutation with its evidence. A fix is a new commit, and runs the checks of step 2 again.

Done when each angle accepts, or each finding has a fix or a refutation.

### 4. Open the pull request

For an issue, check the link from the spec to the code. Each scenario name of the issue is in `specs/features/`, and each name under `## Removed scenarios` is in no file there.

```
gh issue view <n> --json body -q .body | tr -d '\r' | sed -nE 's/^ *Scenario( Outline)?: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" || echo "missing: $name"; done
gh issue view <n> --json body -q .body | tr -d '\r' | sed -n '/^## Removed scenarios/,/^## /p' | sed -nE 's/^- `[^`]*`: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" && echo "still present: $name"; done
```

Push the branch. Open the pull request with the commit's title, or, for a failed run's pull request, put the same text in a comment there. The body states what changed, the result of each suite, each reviewer's verdict, each finding with its fix or its refutation, and, for an issue, the line `Closes #<n>`.

```
gh pr create --title '<title>' --body-file <file>
```

Done when the check above prints nothing, the pull request is open, its body holds each reviewer's verdict and each finding with its fix or its refutation, and its checks are green. The owner merges it. The worktree stays until the merge, for a fix the owner asks for. After the merge, `git worktree remove .claude/worktrees/<n>-<slug>` removes the worktree, and `gh issue edit <n> --remove-label building` closes the claim on an issue.
