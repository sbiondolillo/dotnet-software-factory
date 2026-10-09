---
name: write-issue
description: Write an issue that the factory builds, judge the draft, and file it. Use when a request is to become an issue for this repo, or when a draft or a filed issue is to be checked or revised.
---

`docs/write-an-issue.md` holds every rule a body follows. [`PROOF.md`](PROOF.md) holds the proof of a `## Check`. [`REVIEW.md`](REVIEW.md) holds the angles that judge a draft. This skill ends when the issue is filed, with its URL in the handover. The repo's owner applies the label that starts a run.

## 1. Pick the recipe

Read `## Pick the recipe` of `docs/write-an-issue.md`. Done when the change fits one row. A change that fits two rows is two issues.

## 2. Read `main` for each claim

A claim is each fact the draft states about the code: a file, a name, a command, a text a document holds, a test that stays unchanged. Open the file on `main` that holds each one, and keep its path and line.

For each text the draft replaces or removes, find every test that reads it:

```
git grep -n -F '<old text>' origin/main -- specs tests
```

Done when each claim names its line on `main`. Each scenario a hit is in is under `## Removed scenarios`, or in a block of the draft with every line of its new text.

## 3. Write the draft

Read the section of `docs/write-an-issue.md` for the recipe, then write the title and the body to a file. A behaviour draft writes its scenarios as blocks under `## Scenarios`: open each feature file it names on `main` first, and reuse each step text that `tests/Specs/Steps/` binds when the step means the same. Done when every row of `## Before you file` passes on the file, the row `proof` aside: step 4 runs it.

## 4. Prove the check

A draft with no `## Check` skips this step. Run the check of the draft in a copy of `main`, once for each run of the table of [`PROOF.md`](PROOF.md) that fits the check, and write each exit status to a proof file. A run that gives another status than its row asks for is a fault of the check: fix the check, and run the whole table again. Done when each run gives the status its row asks for, on one `main` SHA.

## 5. Judge the draft

Run the angles of [`REVIEW.md`](REVIEW.md) on the file, each as a subagent, in parallel: `Body rules`, `Claims against main` and `Testability`, and `Holes` too for a draft with `## Check`. A harness with no subagents runs them one after another in this session. Apply every fix of the verdicts in one pass. A fix to the check runs step 4 again, and each hole becomes a run of its table. Then run the angles again. Done when each verdict is `accept`. A line that two rounds reject goes to the person with both findings.

A verdict that says `no rule` is a gap in `docs/write-an-issue.md`. Apply its fix, and name the finding in the handover with the rule it proposes.

## 6. File it

```
gh issue create --title '<title>' --body-file <file> --label <refactor|cleanup|reframe>
```

An issue with no recipe label takes no `--label`. An issue with `## Check` gets the proof file of step 4 as a comment:

```
gh issue comment <n> --body-file <proof file>
```

Done when the handover holds the URL and each `no rule` finding, and an issue with `## Check` has its proof comment.
