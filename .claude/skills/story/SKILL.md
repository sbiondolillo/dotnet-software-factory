---
name: story
description: Turn a requested change to this repo into a story issue, judge its design, and build it once the owner approves. Use when a change to this repo is requested, or when an issue with the label ready is to be built.
---

A change to this repo goes through two gates. The owner approves a **story** on GitHub before any code is written. The owner merges the pull request after it is built. The story says what the system does after the change, at the level of behaviour. The full detail lives in the branch, one time.

Start at **Design** for a new request. Start at **Build** for an issue that has the label `ready`.

## Design

### 1. Read `main`

Read the code on `main` that the change touches. A claim is each fact the design states or relies on: a class, a member, a command, a text a file holds, a test that stays unchanged. Keep the path and the line that holds each one.

For each text the change replaces or removes, find each scenario that reads it:

```
git grep -n -F '<old text>' origin/main -- specs tests
```

Done when each claim has its line on `main`, and each scenario a hit is in is listed.

### 2. Write the design

Write the design to a file in the session's scratch directory. It stays out of the repo. It holds:

- each file that changes, and what changes in it
- each scenario the change adds, and each one it changes or removes, by its name and its feature file
- each claim, with its line on `main`

Done when a contributor could build the change from the file alone.

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

The role is a real user: a repo owner, a person who runs the console program. Each scenario states what that person observes. A text the change pins in full, such as a template or a page, is named in a `Then` by its path and its key line. The feature file holds the full text.

```
gh issue create --title '<title>' --body-file <file>
```

Done when the issue is filed with no label, and the handover gives its URL and each judge's faults with their fixes. Then stop. The owner reads the story and applies `ready`, or comments a change.

## Build

### 5. Read the approved story

Read the issue and its comments with `gh issue view <n> --comments`. A comment of the owner's changes the story. Read the design file of this session, or write it again by steps 1 and 2.

Done when the design matches the issue and every comment of the owner's.

### 6. Write the scenarios, then the code

Work on a branch from `main`: `git switch -c <n>-<a short slug>`.

Each scenario of the issue goes into a feature file under `specs/features/` by its name, with each of its `Given`, `When` and `Then` lines. The feature file adds the detail the issue names by path: a doc string with the full text, a table. Run `dotnet test` and see each new scenario fail. Then write the code until it passes.

Done when every check of the `build` job of `.github/workflows/ci.yml` passes on the branch:

```
dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
```

A change to code that calls GitHub, `docker` or Claude Code also runs `dotnet run --no-restore --project tests/Integration`, and the result goes in the pull request.

### 7. Open the pull request

Check the link from the story to the code: each scenario name of the issue is in `specs/features/`.

```
gh issue view <n> --json body -q .body | sed -n 's/^ *Scenario: //p' | while read -r name; do grep -rqF "Scenario: $name" specs/features || echo "missing: $name"; done
```

Push the branch, and open the pull request with the issue's title. The body states what changed, the result of each suite, and the line `Closes #<n>`.

```
gh pr create --title '<the issue title>' --body-file <file>
```

Done when the check above prints nothing, the pull request is open, and its checks are green. The owner merges it.
