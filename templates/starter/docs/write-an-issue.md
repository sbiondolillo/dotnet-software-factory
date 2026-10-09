# Write an issue the factory builds

The factory reads the labels, the title and the body of the issue, and nothing else. A comment on the issue reaches no agent. The plan, the build and the review each read the same text, so a sentence that one of them cannot act on misleads all three.

## Pick the recipe

| The change | Label | The body holds |
|---|---|---|
| A feature, a bug fix, or a new test for behaviour the program already has | none | the story, `**Today.**`, `**What changes.**`, `## Scenarios` when the change adds or edits a scenario, `## Removed scenarios` when it removes one, and `## Context` when a scenario needs a fact of the world |
| A change to the structure of the code that adds no behaviour. Every test stays as it is | `refactor` | prose, then `## Check` when a command can tell the new structure from the old |
| A fix to a test, a config file or a document. The program stays as it is | `cleanup` | prose, then `## Scope` and `## Check` |
| A change to the code, the tests or the config that keeps the behaviour, such as a format sweep or a framework upgrade. Every passing test keeps passing, and the public API of `src/` stays as it is | `reframe` | prose, then `## Scope` and `## Check` |

A bug fix changes the program, so it takes no label. A change that fits two rows is two issues. The label `behavior` beside `refactor` or `cleanup` gets a comment and no run. The label `reframe` beside `behavior`, `refactor` or `cleanup` gets a comment and no run.

A refactor keeps every test as it is, and it can move a public type or member. A reframe can edit a test under `tests/` that its scope names, and it keeps the public API of `src/` as it is.

## The contributor test

The body describes the program, and only the program. A sentence belongs when a contributor who knows the repo and never heard of the factory needs it to do the work. A sentence about the factory, its labels, its gates or its run is cut.

Each name in the title and the body is the name that the repo or the tool uses: `workflow_dispatch`, `Directory.Packages.props`, the argument `name`. A term the repo lacks is replaced by what it means.

The body states results, and the build chooses every means. A means is a package, a class of a library, an algorithm, an argument that no caller reads, or the steps of an operation. Text that the user of the repo gets is a result: a config value, a workflow, a pinned version, a paragraph of a document. State such a text in one of three forms:

| The known text | The form |
|---|---|
| one value | "The base image of `sandbox/Dockerfile` is `node:24-bookworm-slim`." |
| a small file, known in full | "`<path>` holds this text:", then the text as a block |
| a paragraph of a document that replaces one on `main` | "`<path>`, in `<section>`, holds this paragraph in place of the paragraph that starts '<its first words>':", then the paragraph as a block |

In a scenario, the sentence is a `Then` step and the block is its doc string. In prose, the block is a fenced block.

Name what holds after the change. State an invariant as the state it keeps: "Every other existing test passes unchanged." `**Today.**` is the one place that states what `main` does now.

## A behaviour body

The title states what the person can do after the change, or the end state, in the present tense: "A repo owner can run an issue against any factory ref". The pull request takes the title of the issue.

| Part | What it holds |
|---|---|
| the story | `As a <role>, I want to <ability>, so that <benefit>.` The role is a real user of the program: a repo owner, a person who runs the console program. A story with no real role is a sign that the change is a refactor, a cleanup or a reframe. |
| `**Today.**` | What `main` does now that stops that person, in 3 sentences or fewer. |
| `**What changes.**` | Each file and each program that a scenario names, with the kind of each thing. Then each invariant. |
| `## Scenarios` | One fenced block for each feature file that the change adds or edits. The scenarios are the tests, word for word. A change that only removes scenarios has no `## Scenarios`. |
| `## Removed scenarios` | One list item for each scenario that the change removes. |
| `## Context` | Facts of the world that a scenario needs, each with its source. |

**A block starts with the path of its feature file.** The first line of each fenced block under `## Scenarios` is `# specs/features/<name>.feature`. The name starts with an ASCII letter or a digit, and holds ASCII letters, digits, `.`, `_` and `-`. Each feature file has one block. A fenced block that starts with this line sits under the heading `## Scenarios`, with no other heading between the two.

**The feature file on `main` picks the form of its block.**

| The feature file | The block holds |
|---|---|
| is new | the whole file: its `Feature:` line, then its scenarios |
| is on `main`, and the change edits its `Feature:` line, its `Background:` or a `Rule:` | the whole file |
| is on `main`, and holds a `Rule:` | the whole file |
| is on `main`, and the change edits only scenarios | the scenarios that the change adds or edits, and no `Feature:` line |

A block that holds a `Feature:` line is a whole-file block. It replaces the file on `main`, so it holds every scenario that the file keeps.

In a block of scenarios, each scenario goes into the file by its name. A scenario whose name the file holds replaces that scenario whole, with its tags, its comments and its `Examples:`. So the block holds every line of that scenario, the unchanged ones too. A scenario with a new name goes at the end of the file. So a renamed scenario is a new scenario, and its old name goes under `## Removed scenarios`. Each line between the path line and the first scenario is blank, a tag or a comment. Each block indents its scenarios as the feature files do: `Scenario:` at 2 spaces, and each step at 4.

This body adds a scenario to `specs/features/greeting.feature` and adds the new file `specs/features/farewell.feature`:

````markdown
## Scenarios

```gherkin
# specs/features/greeting.feature
  Scenario: An empty name gets the greeting of the world
    Given the name ""
    When the greeter greets
    Then the greeting is "Hello, world!"
```

```gherkin
# specs/features/farewell.feature
Feature: Farewell

  Scenario: The greeter says goodbye by name
    Given the name "Ada"
    When the greeter says goodbye
    Then the farewell is "Goodbye, Ada!"
```
````

**`## Removed scenarios` holds one list item for each scenario that the change removes.** The item is `- `, the path in backticks, a colon, and the name of the scenario: `` - `specs/features/greeting.feature`: An empty name gets the greeting of the world ``. Each line under the heading is such an item. A feature file whose last scenario is removed leaves the repo, and at least one feature file stays. A whole-file block leaves out each scenario that it removes, so `## Removed scenarios` names no scenario of that file. A name under `## Removed scenarios` is in no block.

**Each scenario states one behaviour that a person observes.**

- Each `Then` states one observable result.
- A step names the real program and the real service: "Given the exchange rate service answers the status 503". The stub is how the test builds that world.
- The `Given` and `When` steps state every input that a `Then` reads. A count, an exit code or a summary line names each input behind it.
- A scenario states each input that the program refuses, byte for byte.
- A condition that picks one member of a set gets one scenario per member, or a `Scenario Outline:` with one row of `Examples:` per member. A condition that holds for every member at once is one scenario.
- Each sad path wanted is its own scenario. A failure is stated by kind, and the text of a program's error is a result that a `Then` states.
- A document edit that the change owes is a scenario whose `Then` names the file, the section and what it holds after the change.
- Each requirement is stated once, in one scenario. A value in the prose that no scenario holds is a requirement that the tests miss.
- A `Then` pins only what a consumer reads: a config value has its program, a file has its reader. An order, a sort or a path form that no caller reads is a means.
- A step text that a class of `tests/Specs/Steps/` already binds runs that binding. Use that text for the same meaning, and other words for a new meaning.
- Each name of a scenario gives its own test name. Two names that differ only in case, punctuation or accents can give one: 'A name gets a greeting' and 'A name, gets a greeting' give one, and so do 'Café' and 'Cafe'.
- A whole-file block parses as Gherkin, and a block of scenarios parses as Gherkin under a `Feature:` line. A doc string sits between two `"""` lines, and its text starts at the column of its opening `"""`.

**`## Context` holds facts of the world.** A fact is what a real service answers, a value a stub reads, or a gap between a scenario and the test suite: a fixture the suite lacks, or a thing it cannot run, such as a workflow. Each fact names its source: a file on `main`, or the request that got the answer, with its date. A row states only what the probe printed, and only what a scenario reads. A sentence that says what the program must do is a requirement, and it goes in a scenario.

## A refactor body

The body starts "Refactor." and a plain imperative. It states what moves and where, the invariant, and each constraint that the tests impose, such as a name that must stay reachable at its old path. The build leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is, so the issue names no change to a test.

`## Check` is optional. When present, it holds one fenced `sh` block that fails on `main` and exits with the code 0 after the change.

## A cleanup body

The body starts "Cleanup." and a plain imperative. It states the defect in the test, the config file or the document, what the test must prove, and the invariant. A cleanup that asks for a test names the test, the operation under test, each answer the test gets, and the state it leaves the service in. The build chooses the helper, the form of an assertion and the way a count is written.

`## Scope` names each path the build can change, one for each line. A line is a path, bare or as a list item, and the path can sit in backticks. A directory ends in `/`, and a path with `*` is a glob.

`## Check` holds one fenced `sh` block, the check command. It fails on `main` and exits with the code 0 after the change. The factory runs it with `sh -c` in a copy of the work tree. Chain the lines with `&&`, so the status of every line counts. A command that runs `dotnet` restores its packages first: `dotnet restore --locked-mode`.

## A reframe body

The body starts "Reframe." and a plain imperative. It states the rule or the version that changes, and the paths on `main` that the change touches. It states the invariant: the program keeps its behaviour, and the public API of `src/` stays as it is. The public API is each public type and each public or protected member. A reframe adds none, removes none and changes none.

`## Scope` and `## Check` take the form of a cleanup body. The scope can name `src/` and a test under `tests/`. Each feature file under `specs/features/` stays as it is.

## A check

`## Check` passes the change that the prose asks for, and refuses each change that misses a part of it. The copy of the work tree that the check runs in holds the tracked files only, so it has an `obj/` or a `bin/` only when one is committed.

- Each element that the prose says must change has its own line in the check: each name, each file, each assertion. A change that misses one element fails at that line.
- The check reads results. A change that reaches the result by another means, such as another helper or another form of an assertion, passes.
- A scope names each file whole. A change to one section of a settings file names the file, and the check pins the whole file with `sha256sum -c`.
- The prose states each fix that the obvious tool gets wrong, with the tool and its wrong result: "`dotnet format` gives `output` the type `string?`, and its type is `string`."

**A check that raises an analyzer rule** chains these parts with `&&`, in this order:

1. A `test -z` of a `find` for `obj` and `bin`. A committed `obj/*.props` reaches the copy this way, and MSBuild imports it.
2. `dotnet restore --locked-mode`.
3. A `sha256sum -c` of each settings file and each project file, with the hash of its text after the change: `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props` and each `*.csproj`.
4. A `test` that the list of each `.editorconfig`, `*.globalconfig`, `Directory.Build.*`, `*.props`, `*.targets`, `*.user` and `*.ruleset` file outside `obj/` and `bin/` equals the list on `main`. `dotnet build` reads a `Directory.Build.rsp` as its arguments, such as `-p:TreatWarningsAsErrors=false`. MSBuild imports a `*.csproj.user` with no reference, and `IDE000%38` is its escape of `IDE0008`.
5. A `! grep -rn -E` over `src` and `tests`, with `--exclude-dir=obj --exclude-dir=bin`, for each rule id and each form that hides code from an analyzer: `pragma`, `autogenerated`, `auto-generated`, `GeneratedCode`, `#[[:space:]]*line`, `#[[:space:]]*nullable`, `SuppressMessage` and `\\[uU][0-9A-Fa-f]{4}`. A C# Unicode escape spells each of these words past the grep.
6. A `! find` over `src` and `tests`, with `-iname`, for each generated name: `*.g.cs`, `*.g.i.cs`, `*.designer.cs`, `*.generated.cs` and `TemporaryGeneratedFile_*`. The analyzers skip generated code, and they match these names in any case.
7. For a rule with no code fix, the counts and the fixed lines. Each count reads the `*.cs` files outside `obj/` and `bin/`. The token of the fix, such as `StringComparison.Ordinal`, counts its uses on `main` plus one for each finding. The `Assert.` members under `tests/` count as on `main`, because a deleted assertion clears its finding. A comment pads a count, so the check pins each fixed line whole with `grep -qxE` when the findings are few. A pinned line inside an inactive `#if` or a raw string still matches, so the check refuses `/*`, `#[[:space:]]*if` and `"""` with `! grep -rnE --include='*.cs' --exclude-dir=obj --exclude-dir=bin` over `src` and `tests`.
8. `dotnet build --no-restore`.

## Before you file

Read the draft once for each row. File it when every row passes. A person with Claude Code runs the skill `write-issue` in `.claude/skills/`, which makes this pass, proves the check, and judges the draft with subagents.

| Row | How to read it | Pass |
|---|---|---|
| factory text | search for a label, a gate, a run, a phase, or a pointer at `AGENTS.md` | zero found |
| story | read the first sentence | it holds a role, an ability and a benefit, and the role is a real user of the program |
| today | read `**Today.**` | it states what `main` does now, in 3 sentences or fewer |
| coverage | list each file and each program that a scenario names | `**What changes.**` names each one |
| names | list each name of a part of the system | each one is the name that the repo or the tool uses |
| pseudocode | list each sentence that names a step the code takes, a member it calls, a constructor, a test method, or a numbered sequence | the list is empty |
| blocks | each fenced block under `## Scenarios` | it starts with the path line of one feature file, takes the form that the file on `main` picks, and parses as Gherkin, under a `Feature:` line for a block of scenarios. A whole-file block for a file on `main` holds each scenario of that file that the change keeps |
| scenarios | each scenario | it names the real program, and each `Then` states one observable result |
| placeholders | search the draft for each `<...>` placeholder of the template | none is left |
| scenario names | list the names of the scenarios of each feature file after the change | no two give one test name, and no block holds one name twice |
| removed | each line under `## Removed scenarios` | it is a list item: a path in backticks, a colon, and a name that the file on `main` holds |
| conditions | search the scenarios for "When" | each `When` names an event that occurs |
| known text | list each config value, workflow, pinned version and document paragraph that the issue already knows | each one is in a `Then` as one value or as a doc string |
| context | list each sentence and each row of `## Context` | each one states a fact of the world and its source, and none states what the program must do |
| stated once | list each requirement in the draft | each one is in one scenario, and nowhere else |
| positive form | search the prose for "not", "never", "don't", "later" | each sentence found states what holds |
| result inputs | each `Then` whose result is a count, an exit code or a summary line | its scenario states each input that result reads |
| on `main` | read each scenario that the change adds or edits against the code on `main` | `main` fails each one, unless the change is a new test for behaviour the program already has. A scenario that a whole-file block keeps from the file on `main` passes on `main` |
| scope and check | a `cleanup` or a `reframe` body | `## Scope` holds a path, and `## Check` holds one fenced `sh` block |
| proof | a body with `## Check`: run the check in a copy of `main`, then in that copy with the change faked | it exits with a code other than 0 on `main`, and with the code 0 on the faked change |
