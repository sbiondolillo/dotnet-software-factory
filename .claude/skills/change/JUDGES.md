# Judges

Reference for [`change`](SKILL.md), step 3. Each angle is one subagent. A reviewer is read-only: it edits no tracked file, and it files and posts nothing.

Give each reviewer the SHA of the commit, the SHA of the base, the path of the build worktree, and the path of the spec file: the issue's body, or the owner's request. The reviewer works in a clone of its own, and edits nothing in the build worktree. `<scratch>` is an absolute path of its own, `<clone>` is `<scratch>/clone`, and `<HOME>` is `<scratch>/home`:

```
mkdir -p <scratch>/home && git clone -q <worktree> <scratch>/clone && cd <scratch>/clone && git checkout -q <SHA>
```

Run every later command in `<clone>`. The clone's `origin/main` is the local `main` of the checkout, which can lag the base. So each command below takes `<base>`, never `origin/main`. The base can be ahead of the commit the branch started from. So a list of the branch's changes takes three dots, `<base>...<SHA>`, and `<fork>` is `git merge-base <base> <SHA>`.

Each reviewer reports `accept` or `reject`. Then it reports each finding with the file and the line, the fault in one sentence, the evidence as a command and its output, and a fix. A finding with no evidence is not a finding. A nitpick is marked `nitpick`. Then it gives one line for each thing it found clean.

## Conformance

Read each sentence of the spec against the clone, and give the line of the branch that makes it true. An issue states its changes under `**What changes.**`, and a request in the owner's words states them as it does. Read each prose file the spec names as `git diff <base>...<SHA> -- <path>`, against its sentence there. Read each scenario of the spec against its feature file: the same name, and the same steps in the same order.

Done when each sentence, each prose file and each scenario has its line in the branch or its finding.

## Test strength, by mutation

Run the features the spec names once on `<SHA>`, before any mutation. The run prints `Passed!`, with a `Total` equal to the count of scenarios in those features. A run that prints `No test matches the given testcase filter`, or that fails to restore or to build, is a fault of the command, not a result.

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

Then make two or more narrower mutations of the new code. Each one is an edit that the controls of the spec do not cover: a bound off by one, a branch inverted. Run the features for each, and restore the commit after each. A mutation that no scenario fails is a finding, with the scenario that should fail it.

Done when the first run on `<SHA>` has its `Total`, each changed file has its list of scenarios that stay green, and two or more narrower mutations have theirs.

## Regressions and style

- A changed scenario or binding that the spec does not name: `git diff --stat <base>...<SHA> -- specs tests`, read against the spec.
- A caller of removed code, and a scenario of a removed binding: `git grep -n -F '<name>' <SHA>` and `git grep -n -F '<step text>' <SHA> -- specs`.
- A step-text collision: a new step text that a binding of `<base>` already matches, a regex binding included. The new step texts are `git diff -U0 <base>...<SHA> -- specs | grep -E '^\+ *(Given|When|Then|And|But) '`, read against `git grep -n -E '\[(Given|When|Then)\(' <base> -- tests`.
- House style: each added line of `git diff -U0 <base>...<SHA> -- src tests`, read against the neighbouring files and `AGENTS.md` → "The rules that build and style enforce".
- A clean merge: `git merge-tree --write-tree <base> <SHA>` exits 0.

Done when each item has its command and its output, and each hit is a finding or is marked clean.
