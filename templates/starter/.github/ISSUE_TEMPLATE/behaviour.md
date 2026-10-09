---
name: Behaviour
about: A feature, a bug fix, or a new test for behaviour the program already has. The rules are docs/write-an-issue.md.
title: ''
labels: ''
---

As a <role>, I want to <ability>, so that <benefit>.

**Today.** <What `main` does now that stops that person, in 3 sentences or fewer.>

**What changes.** <Each file and each program that a scenario names, with the kind of each thing. Then each invariant.>

## Scenarios

<One block for each feature file. A file on main: the scenarios that the change adds or edits. A new file: its Feature: line first, then its scenarios. The rules are docs/write-an-issue.md.>

```gherkin
# specs/features/<name>.feature
  Scenario: <what the person observes>
    Given <the state>
    When <the event>
    Then <the result>
```

## Context

<A fact of the world that a scenario needs, with its source.>
