---
name: story
description: Turn a requested change to this repo into a story issue, judge its draft, and build it once the owner approves. Use when a change to this repo is requested, or when an issue with the label ready is to be built.
---

A change to this repo goes through two gates. The owner approves a **story** on GitHub before a build starts. The owner merges the pull request after it is built. The story says what the system does after the change, at the level of behaviour. The full detail lives in the branch, one time.

The body of the issue is the **handoff**. It holds everything a build needs: the story, what changes, the scenarios, and `## Context` for each fact of the world and for the branch of a prototype. So a session builds from the issue number alone, in any worktree, on any machine. The owner reads one body, with no comment under it.

Start at **Draft** for a new request. Start at **Build** for one issue that has the label `ready`. Start at **Build a batch** to build every story that is ready.

## Draft

### 1. Read `main`

Read the code on `main` that the change touches. A claim is each fact the body states or relies on: a class, a member, a command, a text a file holds, a test that stays unchanged. Keep the path and the line that holds each one.

For each text the change replaces or removes, find each scenario that reads it:

```
git grep -n -F '<old text>' origin/main -- specs tests
```

Done when each claim has its line on `main`, and each scenario a hit is in is listed.

### 2. Write the draft

Write the title and the body to a file in the session's scratch directory. The title states the end state in the present tense: "A repo made from the starter holds the template and the rules of a `reframe` issue". The pull request takes the same title. The body has these parts, in this order:

| Part | What it holds |
|---|---|
| the story | `As a <role>, I want to <ability>, so that <benefit>.` The role is a real user: a repo owner, a person who runs the console program. |
| `**Today.**` | What `main` does now that stops that person, in 3 sentences or fewer. |
| `**What changes.**` | Each file and each program that a scenario names, with the kind of each thing and what it does after the change. Then each invariant, such as "Every other test passes unchanged". 6 sentences or fewer. |
| `## Scenarios` | One fenced `gherkin` block for each feature file the change adds or edits, whose first line is `# specs/features/<name>.feature`. The scenarios are the tests, word for word, each with its steps. A doc string or a table that a step needs is in the block. |
| `## Removed scenarios` | One list item for each scenario the change removes: its feature file's path, a colon, and its name. Absent when there is none. |
| `## Context` | Its first line is `` `main` at `<full SHA>` ``. Then each fact of the world that a scenario or a build needs, with its source: a line on `main`, a probe with its date, a decision with its reason. The prototype's branch and its diff, when one exists. A hunch of a judge is marked `unproven`. |

The body holds no file whole and no block of source code, whole or in part: not a file, a member or a line. A code change is named by its path, its member or its key, and what it does after the change. A string the code writes, such as a message or a line of output, is quoted. A change to a text that a scenario pins, such as a skill, a template or a page, goes on the prototype. The owner reads it there as a diff. A scenario that pins a file whole by a doc string is replaced by steps on its key lines, `the file {string} of this repo holds a line that holds {string}` and `holds no line that holds`, and the prototype holds the file. A doc string in a block is the text a step compares, not a file of the repo. Each scenario states what a real user observes. A step text that a class of `tests/Specs/Steps/` already binds runs that binding: use that text for the same meaning, and other words for a new meaning.

A **prototype** is built for two reasons only: a change to a text that a scenario pins, so the owner reads a diff, or a hole that a judge named with its scenario, its break and its command. This session builds it in a scratch worktree of `main`, `git worktree add --detach <scratch dir> <main SHA>`. Any other draft has no prototype. Code is not such a text: a scenario pins what code does. `## Context` reports what it showed, in a table: each mutation, and the scenario that failed it or the word `pass`. The code of the prototype stays out of the body. The rows are the prototype as it is, each changed file of `main` put back on its own, and each narrower mutation. A prototype for a named hole has a row for the break the judge named, and a scenario of the draft fails it. Commit the prototype when it passes the checks of step 6, and keep its worktree for step 4. Discard it otherwise.

Done when a contributor with no access to this session could build the change from the body and the prototype alone.

### 3. Judge the draft

Run the three angles of [`JUDGES.md`](JUDGES.md) → "Judge a draft" on the file, as three subagents in parallel. Read each fault against `main` before you act on it. Apply every fix in one pass. Run step 1 again for a claim the fix changes. Build the prototype of step 2 for a hole that a judge named with its scenario, its break and its command. A hunch marked `unproven` goes into `## Context` as it is, and gets no prototype. The judges' own proofs are not the prototype.

Done when each fault is fixed in the draft or refuted with its evidence.

### 4. File the story

```
gh issue create --title '<title>' --body-file <body file>
```

When a prototype exists, push it, add its line to `## Context` of the body file, and write the body back. The worktree is detached, so the push names the full ref. The URL pins the `main` SHA, so it reads the same after `main` moves, and the owner reads each changed file there as a diff.

```
git -C <prototype worktree> push origin HEAD:refs/heads/design/<n>
gh issue edit <n> --body-file <body file>
```

The line is `` Prototype at `design/<n>`: https://github.com/{owner}/{repo}/compare/<main SHA>...design/<n> ``.

When another story must merge first, mark the new story as blocked by it, in GitHub's issue relationships:

```
gh api repos/{owner}/{repo}/issues/<n>/dependencies/blocked_by -F issue_id=$(gh api repos/{owner}/{repo}/issues/<blocker> -q .id)
```

A change the owner comments goes into the body, by steps 1 to 3 and `gh issue edit <n> --body-file`. The build reads the body alone. A change to the code pushes the prototype again, `git -C <prototype worktree> push --force origin HEAD:refs/heads/design/<n>`, or removes its line from `## Context`.

Done when the issue is filed with no label, and the handover gives its URL. Then stop. The owner reads the story and applies `ready`, comments a change, or closes it. A closed story's prototype branch is removed: `git push origin --delete design/<n>`.

## Build

### 5. Claim the story and read it

Claim the issue before any other work. An issue that already has the label `building` belongs to another session: stop, and say so.

```
gh issue edit <n> --add-label building
```

Read the issue and its comments with `gh issue view <n> --comments`. The body is the handoff. A comment of the owner's that the body does not hold yet changes the story. When `main` moved since the SHA that `## Context` names, read `git diff <that SHA> origin/main --stat`, and run step 1 again for each claim on a changed file. A body with no `**What changes.**` or no `## Scenarios` gets them by steps 1 to 3, then `gh issue edit <n> --body-file <body file>`, and the push of step 4 when a prototype was made.

Done when the body matches every comment of the owner's and `origin/main`.

### 6. Write the scenarios, then the code

Work in a worktree of your own, on a branch from `<start>`, and run every later command there. `<start>` is `origin/main`, or `origin/design/<n>` when `## Context` names a prototype:

```
git fetch origin && git worktree add .claude/worktrees/<n>-<a short slug> -b <n>-<a short slug> <start>
```

From a prototype, when `main` moved since the SHA that `## Context` names, run `git rebase origin/main` first. The checks below run on the rebased branch.

Each block of `## Scenarios` goes into its feature file under `specs/features/`, word for word. A block that holds a `Feature:` line replaces its feature file. Any other block edits the file on `main`: a scenario whose name the file holds is replaced whole, and a new name goes at the end. Each name under `## Removed scenarios` is removed from its feature file. Run `dotnet test` and see each new scenario fail. Then write the code until it passes. From a prototype, a scenario that passes is the prototype's: read it against the issue. A scenario that fails gets code as above. A scenario of the branch that no block of `## Scenarios` holds is removed.

Then run every check of the `build` job of `.github/workflows/ci.yml` on the branch:

```
dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
```

A change to code that calls GitHub, `docker` or Claude Code also runs `dotnet run --no-restore --project tests/Integration`, and the result goes in the pull request.

Commit the branch, new files included, so that a reviewer reads every change by one SHA:

```
git add -- <each path that the body names> && git commit -m '<the issue title>'
```

From a prototype with no new change, the prototype's commit is the branch's commit, and `git commit` exits with the code 1 and an empty standard error: skip it.

Done when every check above passes, the result of the integration suite is kept when that suite ran, and `git status --porcelain` lists no path that the body names.

### 7. Review the branch

Run the three angles of [`JUDGES.md`](JUDGES.md) → "Judge a branch" on the commit, as three subagents in parallel, on Opus. The branch is not pushed, so each reviewer clones this worktree into a scratch directory of its own and checks the SHA out there. Give each one:

- the full SHA of the commit, from `git rev-parse HEAD`
- the full SHA of the base, from `git fetch -q origin && git rev-parse origin/main`
- the path of this worktree
- the issue number, and the path of a file that holds its body
- the full SHA of the prototype, from `git rev-parse origin/design/<n>`, when `## Context` names one

Read each finding against the branch before you act on it. Each finding gets a fix on the branch or a refutation with its evidence. A fix is a new commit, and runs the checks of step 6 again.

Done when each angle accepts, or each finding has a fix or a refutation.

### 8. Open the pull request

Check the link from the story to the code: each scenario name of the issue is in `specs/features/`, and each name under `## Removed scenarios` is in no file there.

```
gh issue view <n> --json body -q .body | tr -d '\r' | sed -nE 's/^ *Scenario( Outline)?: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" || echo "missing: $name"; done
gh issue view <n> --json body -q .body | tr -d '\r' | sed -n '/^## Removed scenarios/,/^## /p' | sed -nE 's/^- `[^`]*`: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" && echo "still present: $name"; done
```

Push the branch, and open the pull request with the issue's title. The body states what changed, the result of each suite, each reviewer's verdict, each finding with its fix or its refutation, and the line `Closes #<n>`.

```
gh pr create --title '<the issue title>' --body-file <file>
```

Done when the check above prints nothing, the pull request is open, its body holds each reviewer's verdict and each finding with its fix or its refutation, and its checks are green. The owner merges it. The worktree stays until the merge, for a fix the owner asks for. After the merge, `git worktree remove .claude/worktrees/<n>-<slug>` removes it, and `git push origin --delete design/<n>` removes a prototype branch.

## Build a batch

List the stories that are ready to build: the label `ready`, no label `building`, and no open issue that blocks it.

```
gh api graphql -F owner='{owner}' -F name='{repo}' -f query='query($owner:String!,$name:String!){repository(owner:$owner,name:$name){issues(states:OPEN,labels:["ready"],first:100){nodes{number title labels(first:20){nodes{name}} blockedBy(first:20){nodes{number state}}}}}}' -q '.data.repository.issues.nodes[] | select([.labels.nodes[].name] | index("building") | not) | select([.blockedBy.nodes[] | select(.state == "OPEN")] | length == 0) | "\(.number)\t\(.title)"'
```

The query reads each blocker's state, and leaves out a story that an open issue blocks. It waits for the next batch. `issueDependenciesSummary` lags a new relationship by some seconds, so the query reads the `blockedBy` list itself.

Then list the prototype branches, `git ls-remote origin 'refs/heads/design/*'`, and remove each one whose issue is closed: `gh issue view <n> --json state -q .state` is `CLOSED`, and `git push origin --delete design/<n>` removes it.

Give each story that is ready to its own builder: a subagent with its own worktree, or a separate session. Give it one line, `Build #<n> with the story skill.`, and nothing more. The issue holds the rest. Two stories that change the same file are built one after the other, in the order of their numbers.

Done when each story that is ready has a builder, and the handover lists each one with its builder and, once open, its pull request.
