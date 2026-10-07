---
name: write-issue
description: Write an issue that the factory builds, judge the draft, and file it. Use when a request is to become an issue for this repo, or when a draft or a filed issue is to be checked or revised.
---

`docs/write-an-issue.md` holds every rule a body follows. [`REVIEW.md`](REVIEW.md) holds the three angles that judge a draft. This skill ends when the issue is filed, with its URL in the handover. The repo's owner applies the label that starts a run.

## 1. Pick the recipe

Read `## Pick the recipe` of `docs/write-an-issue.md`. Done when the change fits one row. A change that fits two rows is two issues.

## 2. Read `main` for each claim

A claim is each fact the draft states about the code: a file, a name, a command, a text a document holds, a test that stays unchanged. Open the file on `main` that holds each one, and keep its path and line.

For each text the draft replaces or removes, find every test that reads it:

```
git grep -n -F '<old text>' origin/main -- specs tests
```

Done when each claim names its line on `main`. Each scenario a hit is in is named in the draft as removed or replaced, or it is kept with each of its `Then` lines as a criterion.

## 3. Write the draft

Read the section of `docs/write-an-issue.md` for the recipe, then write the title and the body to a file. Done when every row of `## Before you file` passes on the file.

## 4. Judge the draft

Run the three angles of [`REVIEW.md`](REVIEW.md) on the file, as three subagents in parallel. A harness with no subagents runs them one after another in this session. Apply every fix of the three verdicts in one pass, then run the three angles again. Done when each of the three verdicts is `accept`. A line that two rounds reject goes to the person with both findings.

A verdict that says `no rule` is a gap in `docs/write-an-issue.md`. Apply its fix, and name the finding in the handover with the rule it proposes.

## 5. File it

```
gh issue create --title '<title>' --body-file <file> --label <refactor|cleanup>
```

An issue with no recipe label takes no `--label`. Done when the handover holds the URL and each `no rule` finding.
