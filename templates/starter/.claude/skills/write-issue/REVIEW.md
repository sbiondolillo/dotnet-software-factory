# Judge a draft

Reference for step 4 of [`write-issue`](SKILL.md). Each angle is one subagent. It gets the path of the draft, reads the repo at `main`, and writes one verdict.

## The verdict

```
verdict: accept | reject
findings:
- line: <the line of the draft>
  rule: <a heading or a row of docs/write-an-issue.md> | no rule
  fault: <what is wrong, in one sentence>
  fix: <the text that replaces the line>
```

`accept` has zero findings. A finding with `no rule` names a fault that no rule of `docs/write-an-issue.md` covers, and its `fix` holds the rule it proposes beside the text.

## The angles

**Body rules.** Read every rule of `docs/write-an-issue.md` and every row of its `## Before you file` table against the draft, one at a time. Done when each rule and each row has been read against every sentence it governs.

**Claims against `main`.** Read each claim the draft makes about the code against the file on `main` that holds it. Read each bullet against `main`, and trace what `main` does after the bullet's event. A bullet `main` already meets is a finding. For each text the draft replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the draft names each scenario a hit is in. For each stream or file a bullet adds lines to, find each test that counts its lines, and check that the draft names it. Done when each claim names its line on `main`, and each test the change makes false is named.

**Testability.** Read each bullet as the test suite reads it. Each test of `tests/Specs/` answers every call to a service from a stub, so each bullet states a behaviour that a stub can show. Each canned answer a bullet hands a stub names its source in `## Context`: a test of `tests/Integration/`, or the request that got it. An answer that no request can produce says so in `## Context`. Done when each bullet has a stub that can show it, and each answer has a source.
