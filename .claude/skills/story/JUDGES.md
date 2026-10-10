# Judges

Reference for [`story`](SKILL.md). "Judge a draft" is step 3, and "Judge a branch" is step 7. Each angle is one subagent. A judge is read-only: it edits no tracked file, and it files and posts nothing.

## Judge a draft

Give each judge the path of the draft, the request in the owner's words, the `main` SHA, and the path of the prototype worktree when one exists.

Each judge reports `accept` or `reject`, and `accept` has no fault. Then it reports each fault with the quoted text, the evidence as a path and a line, and the replacement text. Then it gives one line for each thing it found clean.

### Claims against `main`

Read each claim of the draft against the line on `main` that holds it. Read each new scenario against `main`, and trace what `main` does after its event: a scenario `main` already passes is a fault. For each text the change replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the draft names each scenario a hit is in. For each stream or file the change adds lines to, find each test that counts its lines.

Done when each claim names its line on `main`, and each test the change makes false is named.

### Holes

This angle runs only on a draft that holds a check, or a change to a text that a scenario pins. A plain behaviour draft does not get it: its sad paths are the owner's to state, and a builder that can read the repo asks.

Find each way a build passes every scenario of the draft and still misses the request: a check that accepts too much, a text pinned in part where a reader reads all of it, a sad path the request implies and no scenario states. A hole is proven in a scratch worktree of `main` only when the judge names three things: the scenario, the break that keeps it green, and the one command that settles it. Then report the command and its output, not the code of the proof. A hunch with no such command is reported as a sentence for `## Context`, marked `unproven`.

A check pins the claim of the story, not the program. A break that the build or the test suite catches is not a hole. `Holes` accepts when each remaining hole needs a change that no scenario of the request implies. Such a hole is reported as a line for `## Context`, not as a fault.

Done when each hole names the scenario or the line of the draft that closes it, or the change it needs that no scenario of the request implies.

### The story

Read the draft as the owner reads it. The story asks for the change the request asks for, and no more. Each part of the body holds what step 2 of `SKILL.md` says it holds, and no more: `**What changes.**` names parts in 6 sentences or fewer, and `## Context` holds facts with their sources, or a hunch marked `unproven`, and no requirement. Each scenario states what a real user observes, in the names the code and the tools use. Each change of the body is reached by a scenario, or is named as an invariant. A file held whole in the body, or a block of source code, whole or in part, is a fault: the body names the path, the member and the behaviour, and the prototype holds the text.

Done when each sentence of the body and each scenario has been read against the request.

## Judge a branch

Give each reviewer the SHA of the commit, the SHA of the base, the path of the build worktree, the path of the file that holds the issue's body, and the SHA of the prototype when `## Context` names one. The reviewer works in a clone of its own, and edits nothing in the build worktree. `<scratch>` is an absolute path of its own, `<clone>` is `<scratch>/clone`, and `<HOME>` is `<scratch>/home`:

```
mkdir -p <scratch>/home && git clone -q <worktree> <scratch>/clone && cd <scratch>/clone && git checkout -q <SHA>
```

Run every later command in `<clone>`. The clone's `origin/main` is the local `main` of the checkout, which can lag the base. So each command below takes `<base>`, never `origin/main`. The base can be ahead of the commit the branch started from. So a list of the branch's changes takes three dots, `<base>...<SHA>`, and `<fork>` is `git merge-base <base> <SHA>`.

Each reviewer reports `accept` or `reject`. Then it reports each finding with the file and the line, the fault in one sentence, the evidence as a command and its output, and a fix. A finding with no evidence is not a finding. A nitpick is marked `nitpick`. Then it gives one line for each thing it found clean.

### Conformance

Read each sentence of `**What changes.**` of the issue against the clone, and give the line of the branch that makes it true. Read each text a scenario pins as `git diff <base>...<SHA> -- <path>`, against `**What changes.**`. Read each scenario of the issue against its feature file: the same name, and the same steps in the same order.

Done when each sentence, each text and each scenario has its line in the branch or its finding.

### Test strength, by mutation

Run the features the story names once on `<SHA>`, before any mutation. The run prints `Passed!`, with a `Total` equal to the count of scenarios in those features. A run that prints `No test matches the given testcase filter`, or that fails to restore or to build, is a fault of the command, not a result.

```
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp/home -v <HOME>:/tmp/home -v <clone>:<clone> -w <clone> mcr.microsoft.com/dotnet/sdk:<major.minor> sh -c 'dotnet restore --locked-mode && dotnet test --no-restore --filter "FullyQualifiedName~<Class>"'
```

The image tag is the major and minor version of `global.json`, such as `10.0`. Docker makes a missing `<HOME>` owned by root, and the restore then fails, so the clone block makes it first. Each run uses the same `<HOME>`, so a restore reads the package cache of the run before it. `<Class>` is the class Reqnroll compiles for the feature, and punctuation changes it: the title `A repo made from the starter holds the skill write-issue` gives `ARepoMadeFromTheStarterHoldsTheSkillWrite_IssueFeature`. Read it after the first run:

```
grep -rhoE 'partial class \w+' tests/Specs/obj --include='<file>.feature.cs'
```

Then list the changed files: `git diff --name-only <base>...<SHA>`. For each one, put the version of `<fork>` back on its own, run the features, and list each pinned scenario that stays green. Then restore the commit. A file that is new in the change is removed with `git rm -q <path>` in place of the checkout from `<fork>`. A run with the feature file itself removed has no scenario, which is its result.

```
git checkout <fork> -- <path>
<the docker run above>
git reset -q --hard <SHA>
```

A scenario that stays green with a file of the change put back is a finding: it pins nothing in that file.

When `<SHA>` is the SHA of the prototype, the table of `## Context` stands for the runs with each file put back. The reviewer still makes the first run, to build `tests/Specs/obj` and read `<Class>`, and checks that its `Total` agrees with the table. Then it makes only the narrower mutations below. A file of `git diff --name-only <base>...<SHA>` with no row in the table gets the run with that file put back, as above.

Then make two or more narrower mutations of the new code. Each one is an edit that the controls of the body do not cover: a bound off by one, a branch inverted, a line of a pinned text changed. Run the features for each, and restore the commit after each. A mutation that no scenario fails is a finding, with the scenario that should fail it.

Done when the first run on `<SHA>` has its `Total`, each changed file has its list of scenarios that stay green or its row in the table of `## Context`, and two or more narrower mutations have theirs.

### Regressions and style

- A changed scenario or binding that the story does not name: `git diff --stat <base>...<SHA> -- specs tests`, read against `**What changes.**`.
- A caller of removed code, and a scenario of a removed binding: `git grep -n -F '<name>' <SHA>` and `git grep -n -F '<step text>' <SHA> -- specs`.
- A step-text collision: a new step text that a binding of `<base>` already matches, a regex binding included. The new step texts are `git diff -U0 <base>...<SHA> -- specs | grep -E '^\+ *(Given|When|Then|And|But) '`, read against `git grep -n -E '\[(Given|When|Then)\(' <base> -- tests`.
- House style: each added line of `git diff -U0 <base>...<SHA> -- src tests`, read against the neighbouring files and `AGENTS.md` → "The rules that build and style enforce".
- A clean merge: `git merge-tree --write-tree <base> <SHA>` exits 0.

Done when each item has its command and its output, and each hit is a finding or is marked clean.
