# Judge a draft

Reference for step 5 of [`write-issue`](SKILL.md). Each angle is one subagent. It gets the path of the draft, reads the repo at `main`, and writes one verdict. The angle `Holes` also gets the proof file of step 4.

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

**Body rules.** Read every rule of `docs/write-an-issue.md` and every row of its `## Before you file` table against the draft, one at a time, the row `proof` aside: step 4 runs it. Done when each rule and each row has been read against every sentence it governs.

**Claims against `main`.** Read each claim the draft makes about the code against the file on `main` that holds it. Read each scenario that the change adds or edits against `main`, and trace what `main` does after its `When`. A scenario that `main` already passes is a finding, unless the change is a new test for behaviour the program already has. Read each block against its feature file on `main`: it takes the form that the table of `docs/write-an-issue.md` picks, a whole-file block for a file on `main` holds each scenario of that file that the change keeps, and each name under `## Removed scenarios` is in its file. For each text the draft replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the draft names each scenario a hit is in. For each stream or file a scenario adds lines to, find each test that counts its lines, and check that the draft names it. Done when each claim names its line on `main`, and each test the change makes false is named.

**Testability.** Read each scenario as the test suite reads it. Each test of `tests/Specs/` answers every call to a service from a stub, so each scenario states a behaviour that a stub can show. Each canned answer a `Given` hands a stub names its source in `## Context`: a test of `tests/Integration/`, or the request that got it. An answer that no request can produce says so in `## Context`. Done when each scenario has a stub that can show it, and each answer has a source.

**Holes.** For a draft with `## Check`. Find each way a change exits with the code 0 on the check and misses a part of what the prose asks for: an element with no line of its own, a means that the check reads, a form that hides code from an analyzer, a count that a comment pads. Run each likely hole in a copy of `main` as [`PROOF.md`](PROOF.md) says, and give its exit status in the finding. Done when each likely hole has run, and each hole that exits with the code 0 is a finding whose `fix` holds the line of the check that closes it.
