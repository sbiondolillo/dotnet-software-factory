---
name: story
description: Turn a requested change to this repo into a story issue, judge its design, and build it once the owner approves. Use when a change to this repo is requested, or when an issue with the label ready is to be built.
---

A change to this repo goes through two gates. The owner approves a **story** on GitHub before any code is written. The owner merges the pull request after it is built. The story says what the system does after the change, at the level of behaviour. The full detail lives in the branch, one time.

The issue is the **handoff**. Its body is the story. Its comment `## Design` carries everything a build needs. So a session builds from the issue number alone, in any worktree, on any machine.

Start at **Design** for a new request. Start at **Build** for one issue that has the label `ready`. Start at **Build a batch** to build every story that is ready.

## Design

### 1. Read `main`

Read the code on `main` that the change touches. A claim is each fact the design states or relies on: a class, a member, a command, a text a file holds, a test that stays unchanged. Keep the path and the line that holds each one.

For each text the change replaces or removes, find each scenario that reads it:

```
git grep -n -F '<old text>' origin/main -- specs tests
```

Done when each claim has its line on `main`, and each scenario a hit is in is listed.

### 2. Write the design

Write the design to a file in the session's scratch directory. Step 4 posts it on the issue, and the scratch file is then spent. It opens with the line `## Design`, then the line `` `main` at `<full SHA>` ``, and it holds:

- each file that changes, and what changes in it
- each scenario the change adds, and each one it changes or removes, by its name and its feature file
- each claim, with its line on `main`
- each full text the change pins, such as a template or a page, as a fenced block
- each judge's fault from step 3, with its fix or its refutation

Done when a contributor with no access to this session could build the change from the file alone.

### 3. Judge it

Run the three angles of [`JUDGES.md`](JUDGES.md) on the design, as three subagents in parallel. Read each fault against `main` before you act on it. Apply every fix in one pass, and run step 1 again for a claim the fix changes.

Done when each fault is fixed in the design or refuted with its evidence.

### 4. File the story

The title states the end state in the present tense: "A repo made from the starter holds the template and the rules of a `reframe` issue". The pull request takes the same title. The body has this form:

````markdown
As a <role>, I want to <ability>, so that <benefit>.

**Today.** <What `main` does now that stops that person, in 3 sentences or fewer.>

**What changes.** <Each part of the system that changes, named as the code names it, and what it does after the change. Then each invariant, such as "Every other test passes unchanged".>

## Scenarios

```gherkin
Scenario: <the name the feature file uses>
  Given <the state>
  When <the event>
  Then <what a person observes>
```
````

The role is a real user: a repo owner, a person who runs the console program. Each scenario states what that person observes. A text the change pins in full, such as a template or a page, is named in a `Then` by its path and its key line. The design holds the full text.

When another story must merge first, the body ends with the line `**After:** #<n>`.

File the story, then post the design as a comment on it:

```
gh issue create --title '<title>' --body-file <story file>
gh issue comment <n> --body-file <design file>
```

A design that the owner's comment changes is posted again as a new `## Design` comment. The build reads the last one.

Done when the issue is filed with no label, its `## Design` comment is posted, and the handover gives its URL and each judge's faults with their fixes. Then stop. The owner reads the story and applies `ready`, or comments a change.

## Build

### 5. Claim the story and read it

Claim the issue before any other work. An issue that already has the label `building` belongs to another session: stop, and say so.

```
gh issue edit <n> --add-label building
```

Read the issue and its comments with `gh issue view <n> --comments`. The design is the last comment that starts `## Design`. A later comment of the owner's changes the story. When `main` moved since the SHA the design names, read `git diff <that SHA> origin/main --stat`, and run step 1 again for each claim on a changed file.

Done when the design matches the issue, every comment of the owner's, and `origin/main`. An issue with no `## Design` comment gets one by steps 1 to 3 first.

### 6. Write the scenarios, then the code

Work in a worktree of your own, on a branch from `origin/main`, and run every later command there:

```
git fetch origin && git worktree add .claude/worktrees/<n>-<a short slug> -b <n>-<a short slug> origin/main
```

Each scenario of the issue goes into a feature file under `specs/features/` by its name, with each of its `Given`, `When` and `Then` lines. The feature file adds the detail the issue names by path: a doc string with the full text, a table. Run `dotnet test` and see each new scenario fail. Then write the code until it passes.

Done when every check of the `build` job of `.github/workflows/ci.yml` passes on the branch:

```
dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
```

A change to code that calls GitHub, `docker` or Claude Code also runs `dotnet run --no-restore --project tests/Integration`, and the result goes in the pull request.

### 7. Open the pull request

Check the link from the story to the code: each scenario name of the issue is in `specs/features/`.

```
gh issue view <n> --json body -q .body | tr -d '\r' | sed -nE 's/^ *Scenario( Outline)?: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" || echo "missing: $name"; done
```

Push the branch, and open the pull request with the issue's title. The body states what changed, the result of each suite, and the line `Closes #<n>`.

```
gh pr create --title '<the issue title>' --body-file <file>
```

Done when the check above prints nothing, the pull request is open, and its checks are green. The owner merges it. The worktree stays until the merge, for a fix the review asks for. After the merge, `git worktree remove .claude/worktrees/<n>-<slug>` removes it.

## Build a batch

List the stories that are ready to build: the label `ready`, no label `building`, and no open `**After:**` story.

```
gh issue list --label ready --state open --json number,title,labels,body -q '.[] | select([.labels[].name] | index("building") | not) | "\(.number)\t\(.title)\t\([.body | scan("\\*\\*After:\\*\\* #([0-9]+)")[0]] | join(","))"'
```

A story whose `**After:**` issue is still open waits for the next batch: check each one with `gh issue view <after> --json state -q .state`.

Give each story that is ready to its own builder: a subagent with its own worktree, or a separate session. Give it one line, `Build #<n> with the story skill.`, and nothing more. The issue holds the rest. Two stories that change the same file are built one after the other, in the order of their numbers.

Done when each story that is ready has a builder, and the handover lists each one with its builder and, once open, its pull request.
