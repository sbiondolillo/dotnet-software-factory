# Write an issue the factory builds

The factory reads the labels, the title and the body of the issue, and nothing else. A comment on the issue reaches no agent. The plan, the build and the review each read the same text, so a sentence that one of them cannot act on misleads all three.

## Pick the recipe

| The change | Label | The body holds |
|---|---|---|
| A feature, a bug fix, or a new test for behaviour the program already has | none | the story, `**Today.**`, `**What changes.**`, `## Acceptance criteria`, and `## Context` when a criterion needs a fact of the world |
| A change to the structure of the code that adds no behaviour. Every test stays as it is | `refactor` | prose, then `## Check` when a command can tell the new structure from the old |
| A fix to a test, a config file or a document. The program stays as it is | `cleanup` | prose, then `## Scope` and `## Check` |

A bug fix changes the program, so it takes no label. A change that fits two rows is two issues. The label `behavior` beside `refactor` or `cleanup` gets a comment and no run.

## The contributor test

The body describes the program, and only the program. A sentence belongs when a contributor who knows the repo and never heard of the factory needs it to do the work. A sentence about the factory, its labels, its gates or its run is cut.

Each name in the title and the body is the name that the repo or the tool uses: `workflow_dispatch`, `Directory.Packages.props`, the argument `name`. A term the repo lacks is replaced by what it means.

The body states results, and the build chooses every means. A means is a package, a class of a library, an algorithm, an argument that no caller reads, or the steps of an operation. Text that the user of the repo gets is a result: a config value, a workflow, a pinned version, a paragraph of a document. State such a text in one of three forms:

| The known text | The form |
|---|---|
| one value | "The base image of `sandbox/Dockerfile` is `node:24-bookworm-slim`." |
| a small file, known in full | "`<path>` holds this text:", then the text as a fenced block under the bullet |
| a paragraph of a document that replaces one on `main` | "`<path>`, in `<section>`, holds this paragraph in place of the paragraph that starts '<its first words>':", then the paragraph as a fenced block |

Name what holds after the change. State an invariant as the state it keeps: "Every other existing test passes unchanged." `**Today.**` is the one place that states what `main` does now.

## A behaviour body

The title states what the person can do after the change, or the end state, in the present tense: "A repo owner can run an issue against any factory ref". The pull request takes the title of the issue.

| Part | What it holds |
|---|---|
| the story | `As a <role>, I want to <ability>, so that <benefit>.` The role is a real user of the program: a repo owner, a person who runs the console program. A story with no real role is a sign that the change is a refactor or a cleanup. |
| `**Today.**` | What `main` does now that stops that person, in 3 sentences or fewer. |
| `**What changes.**` | Each file and each program that a criterion names, with the kind of each thing. Each scenario of `specs/features/` that the change removes or replaces, by name. Then each invariant. |
| `## Acceptance criteria` | Plain bullets. Each bullet is one observable result. |
| `## Context` | Facts of the world that a criterion needs, each with its source. |

**A bullet has one of two forms, and its subject picks the form.**

| The bullet is about | Form | Example |
|---|---|---|
| an event and its result | the condition, the actor and the result | "When the argument `name` holds no letter, the console program writes `The name holds no letter.` to standard error and exits with the code 1." |
| the static state of a file | the file and what it holds | "`README.md`, in `## Commands`, documents the argument `name`." |

- A bullet names the real program and the real service: "When the exchange rate service answers the status 503, the console program writes the status to standard error." The stub is how the test builds that world.
- A bullet states every input its result reads. A count, an exit code or a summary line names each input behind it.
- A bullet states each input the program refuses, byte for byte.
- A condition that picks one member of a set gets one bullet per member. A condition that holds for every member at once is one bullet.
- Each sad path wanted is its own bullet. A failure is stated by kind, and the text of a program's error is a result the bullet states.
- A document edit the change owes is a bullet in the static form: the file, the section and what it holds after the change.
- A change to a scenario that `specs/features/` already holds states each `Then` of that scenario as a criterion, the unchanged ones too, and `**What changes.**` names the scenario.
- Each requirement is stated once, in one criterion. A value in the prose that no criterion holds is a requirement the tests miss.
- A bullet pins only what a consumer reads: a config value has its program, a file has its reader. An order, a sort or a path form that no caller reads is a means.

**`## Context` holds facts of the world.** A fact is what a real service answers, a value a stub reads, or a gap between a bullet and the test suite: a fixture the suite lacks, or a thing it cannot run, such as a workflow. Each fact names its source: a file on `main`, or the request that got the answer, with its date. A row states only what the probe printed, and only what a criterion reads. A sentence that says what the program must do is a requirement, and it goes in a criterion.

## A refactor body

The body starts "Refactor." and a plain imperative. It states what moves and where, the invariant, and each constraint that the tests impose, such as a name that must stay reachable at its old path. The build leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is, so the issue names no change to a test.

`## Check` is optional. When present, it holds one fenced `sh` block that fails on `main` and exits with the code 0 after the change.

## A cleanup body

The body starts "Cleanup." and a plain imperative. It states the defect in the test, the config file or the document, what the test must prove, and the invariant. A cleanup that asks for a test names the test, the operation under test, each answer the test gets, and the state it leaves the service in. The build chooses the helper, the form of an assertion and the way a count is written.

`## Scope` names each path the build can change, one for each line. A line is a path, bare or as a list item, and the path can sit in backticks. A directory ends in `/`, and a path with `*` is a glob.

`## Check` holds one fenced `sh` block, the check command. It fails on `main` and exits with the code 0 after the change. The factory runs it with `sh -c` in a copy of the work tree. Chain the lines with `&&`, so the status of every line counts. A command that runs `dotnet` restores its packages first: `dotnet restore --locked-mode`.

## Before you file

Read the draft once for each row. File it when every row passes. A person with Claude Code runs the skill `write-issue` in `.claude/skills/`, which makes this pass and two more with subagents.

| Row | How to read it | Pass |
|---|---|---|
| factory text | search for a label, a gate, a run, a phase, or a pointer at `AGENTS.md` | zero found |
| story | read the first sentence | it holds a role, an ability and a benefit, and the role is a real user of the program |
| today | read `**Today.**` | it states what `main` does now, in 3 sentences or fewer |
| coverage | list each file and each program that a criterion names | `**What changes.**` names each one |
| names | list each name of a part of the system | each one is the name that the repo or the tool uses |
| pseudocode | list each sentence that names a step the code takes, a member it calls, a constructor, a test method, or a numbered sequence | the list is empty |
| bullets | each one has one of the two forms, names the real program, and states one observable result | every bullet |
| conditions | search the criteria for "When" | each clause found names an event that occurs |
| known text | list each config value, workflow, pinned version and document paragraph that the issue already knows | each one is in a criterion as one value or as a block |
| context | list each sentence and each row of `## Context` | each one states a fact of the world and its source, and none states what the program must do |
| stated once | list each requirement in the draft | each one is in one criterion, and nowhere else |
| positive form | search for "not", "never", "don't", "later" | each sentence found states what holds |
| result inputs | each bullet whose result is a count, an exit code or a summary line | the bullet states each input that result reads |
| on `main` | read each bullet against the code on `main` | `main` fails each one. A bullet `main` meets leaves the issue |
| scope and check | a `cleanup` body | `## Scope` holds a path, and `## Check` holds one fenced `sh` block |
