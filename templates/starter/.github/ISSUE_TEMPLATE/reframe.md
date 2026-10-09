---
name: Reframe
about: A change to the code, the tests or the config that keeps the behaviour, such as a format sweep or a framework upgrade. Every passing test keeps passing, and the public API of src/ stays as it is. The rules are docs/write-an-issue.md.
title: ''
labels: reframe
---

Reframe. <The rule or the version that changes, the paths it touches, and the invariant.>

## Scope

- `<path>`

## Check

```sh
<a command that fails on main and exits with the code 0 after the change>
```
