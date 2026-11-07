---
name: code-review
description: Review a pull request of this repo by the angles of the judge. Use on every pull request review.
---

# Code review

Run two angles of `.claude/skills/change/JUDGES.md` on the pull request: `Conformance` and `Regressions and style`. Read the preamble of that file first, then those two sections. Run each command that an angle names. `Test strength, by mutation` needs `docker`, so this review runs the other two angles only.

## The terms of JUDGES.md on a pull request

`JUDGES.md` is written for a reviewer in a local clone. On a pull request, its terms mean this:

| Term | On the pull request |
|---|---|
| `<clone>` | the checkout of the head that you review in. Skip the clone block of the preamble |
| `<SHA>` | the head commit of the pull request |
| `<base>` | the base commit of the pull request, its `base.sha` |
| `<fork>` | `git merge-base <base> <SHA>` |
| the spec | the text that `spec_for` in `.github/judge/judge.py` returns for this pull request: the body of the issue that the body of the pull request names, or the body of the pull request |

A shallow checkout lacks `<base>`. When `git merge-base` or a diff against `<base>` fails, run `git fetch --depth=400 origin <base> <SHA>` once, then the command again.

## The review

Each finding is a review comment on its file and line. The comment holds the fault in one sentence, the evidence as the command and its output, and the fix. A nitpick starts with `nitpick:`. A finding on a line outside the diff goes in the overview, with its file and line.

The overview starts with these lines, in this order:

1. `Conformance: accept` or `Conformance: reject`.
2. `Regressions and style: accept` or `Regressions and style: reject`.
3. `Commands:`, then each command of the two angles, each marked `ran` or `did not run`, with the reason for each one that did not run.

Then the overview lists the clean items, one line each.

Done when each of the two angles meets the `Done when` line of its section in `JUDGES.md`.
