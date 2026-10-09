Feature: A repo made from the starter holds the skill write-issue

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  Scenario: The skill write-issue drafts the scenarios of a behaviour issue
    Then the copy's file ".claude/skills/write-issue/SKILL.md" holds this text, with a line that holds "A behaviour draft writes its scenarios as blocks under `## Scenarios`"
      """
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

      Done when each claim names its line on `main`. Each scenario a hit is in is under `## Removed scenarios`, or in a block of the draft with every line of its new text.

      ## 3. Write the draft

      Read the section of `docs/write-an-issue.md` for the recipe, then write the title and the body to a file. A behaviour draft writes its scenarios as blocks under `## Scenarios`: open each feature file it names on `main` first, and reuse each step text that `tests/Specs/Steps/` binds when the step means the same. Done when every row of `## Before you file` passes on the file.

      ## 4. Judge the draft

      Run the three angles of [`REVIEW.md`](REVIEW.md) on the file, as three subagents in parallel. A harness with no subagents runs them one after another in this session. Apply every fix of the three verdicts in one pass, then run the three angles again. Done when each of the three verdicts is `accept`. A line that two rounds reject goes to the person with both findings.

      A verdict that says `no rule` is a gap in `docs/write-an-issue.md`. Apply its fix, and name the finding in the handover with the rule it proposes.

      ## 5. File it

      ```
      gh issue create --title '<title>' --body-file <file> --label <refactor|cleanup|reframe>
      ```

      An issue with no recipe label takes no `--label`. Done when the handover holds the URL and each `no rule` finding.
      """

  @bullet-2
  Scenario: The copy's REVIEW.md holds the angles that judge a draft
    # run: printf '%s\n' '.claude/skills/write-issue/REVIEW.md'
    Then the copy's file ".claude/skills/write-issue/REVIEW.md" holds this text, with a line that holds "Read each scenario that the change adds or edits against `main`"
      """
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

      **Claims against `main`.** Read each claim the draft makes about the code against the file on `main` that holds it. Read each scenario that the change adds or edits against `main`, and trace what `main` does after its `When`. A scenario that `main` already passes is a finding, unless the change is a new test for behaviour the program already has. Read each block against its feature file on `main`: it takes the form that the table of `docs/write-an-issue.md` picks, a whole-file block for a file on `main` holds each scenario of that file that the change keeps, and each name under `## Removed scenarios` is in its file. For each text the draft replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the draft names each scenario a hit is in. For each stream or file a scenario adds lines to, find each test that counts its lines, and check that the draft names it. Done when each claim names its line on `main`, and each test the change makes false is named.

      **Testability.** Read each scenario as the test suite reads it. Each test of `tests/Specs/` answers every call to a service from a stub, so each scenario states a behaviour that a stub can show. Each canned answer a `Given` hands a stub names its source in `## Context`: a test of `tests/Integration/`, or the request that got it. An answer that no request can produce says so in `## Context`. Done when each scenario has a stub that can show it, and each answer has a source.
      """

  @bullet-3
  Scenario: The skill directory holds SKILL.md and REVIEW.md and no other file
    # run: printf '%s\n' '.claude/skills/write-issue' 'SKILL.md' 'REVIEW.md'
    Then the copy's directory ".claude/skills/write-issue" holds exactly the files "SKILL.md" and "REVIEW.md"

  @bullet-3
  Scenario: The skill directory holds no file besides the two
    # run: printf '%s\n' '.claude/skills/write-issue' 'SKILL.md' 'REVIEW.md'
    Then the copy's directory ".claude/skills/write-issue" holds no file besides "SKILL.md" and "REVIEW.md"

  Scenario: The write-an-issue page holds the rules of a scenario block
    Then the copy's file "docs/write-an-issue.md" holds this text, with a line that holds "**A block starts with the path of its feature file.**"
      """
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

      ## Before you file

      Read the draft once for each row. File it when every row passes. A person with Claude Code runs the skill `write-issue` in `.claude/skills/`, which makes this pass and two more with subagents.

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
      """

  Scenario: The example body of the write-an-issue page applies to the copy
    Given the body in the markdown block of the copy's "docs/write-an-issue.md"
    When the scenarios of that body are applied to the copy
    Then the apply gives no fault
    And the copy holds "specs/features/farewell.feature"
    And the copy's "specs/features/greeting.feature" ends with the scenario "An empty name gets the greeting of the world"
