Feature: A repo made from the starter holds the rules of an issue the factory builds

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  @bullet-1
  Scenario: The copy's write-issue skill holds the steps of the skill
    # run: printf '%s\n' '.claude/skills/write-issue/SKILL.md'
    Then the copy's file ".claude/skills/write-issue/SKILL.md" holds this text
      """
      ---
      name: write-issue
      description: Write an issue that the factory builds, judge the draft, and file it. Use when asked to write, draft, revise or check an issue for this repo, or to turn a request into an issue.
      ---

      The factory reads the labels, the title and the body of the issue, and nothing else. `docs/write-an-issue.md` holds every rule a body follows. [`REVIEW.md`](REVIEW.md) holds the three angles that judge a draft. This skill ends when the issue is filed, with its URL in the handover. The repo's owner starts the run.

      ## 1. Pick the recipe

      Read `## Pick the recipe` of `docs/write-an-issue.md`. Done when the change fits one row. A change that fits two rows is two issues.

      ## 2. Read `main` for each claim

      A claim is each fact the draft states about the code: a file, a name, a command, a text a document holds, a test that stays unchanged. Open the file on `main` that holds each one, and keep its path and line.

      For each text the draft replaces or removes, find every test that reads it:

      ```
      git grep -n -F '<old text>' origin/main -- specs tests
      ```

      Done when each claim names its line on `main`, and each scenario a hit is in is named in the draft as removed, replaced, or kept with each of its `Then` lines as a criterion.

      ## 3. Write the draft

      Read the section of `docs/write-an-issue.md` for the recipe, then write the title and the body to a file. Done when every row of `## Before you file` passes on the file.

      ## 4. Judge the draft

      Run the three angles of [`REVIEW.md`](REVIEW.md) on the file, as three subagents in parallel. A harness with no subagents runs them one after another in this session. Apply every fix of the three verdicts in one pass, then run the three angles again. Done when each of the three verdicts is `accept`.

      A verdict that says `no rule` is a gap in `docs/write-an-issue.md`. Apply its fix, and name the finding in the handover with the rule it proposes.

      ## 5. File it

      ```
      gh issue create --title '<title>' --body-file <file> --label <refactor|cleanup>
      ```

      An issue with no recipe label takes no `--label`. Done when the handover holds the URL and each `no rule` finding.
      """

  @bullet-2
  Scenario: The copy's write-issue skill holds the angles that judge a draft
    # run: printf '%s\n' '.claude/skills/write-issue/REVIEW.md'
    Then the copy's file ".claude/skills/write-issue/REVIEW.md" holds this text
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

      **Claims against `main`.** Read each claim the draft makes about the code against the file on `main` that holds it. Read each bullet against `main`, and trace what `main` does after the bullet's event: a bullet `main` already meets is a finding. For each text the draft replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the draft names each scenario a hit is in. For each stream or file a bullet adds lines to, find each test that counts its lines, and check that the draft names it. Done when each claim names its line on `main`, and each test the change makes false is named.

      **Testability.** Read each bullet as the test suite reads it. Each test of `tests/Specs/` answers every call to a service from a stub, so each bullet states a behaviour that a stub can show. Each canned answer a bullet hands a stub names its source in `## Context`: a test of `tests/Integration/`, or the request that got it. An answer that no request can produce says so in `## Context`. Done when each bullet has a stub that can show it, and each answer has a source.
      """

  @bullet-3
  Scenario: The copy's write-issue skill holds two files and no other
    # run: printf '%s\n' '.claude/skills/write-issue/' 'SKILL.md' 'REVIEW.md'
    Then the copy's directory ".claude/skills/write-issue/" holds the files "SKILL.md" and "REVIEW.md" and no other file

  @bullet-4
  Scenario: The copy's write-an-issue page holds the rules of an issue and points at the skill
    # run: printf '%s\n' 'docs/write-an-issue.md'
    Then the copy's file "docs/write-an-issue.md" holds this text
      """
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
      """

  @bullet-4
  Scenario: The copy's write-an-issue page points at the skill in Before you file
    # run: printf '%s\n' 'docs/write-an-issue.md' '## Before you file' 'Read the draft once for each row'
    Then the copy's file "docs/write-an-issue.md", in "## Before you file", holds this paragraph in place of the paragraph that starts "Read the draft once for each row"
      """
      Read the draft once for each row. File it when every row passes. A person with Claude Code runs the skill `write-issue` in `.claude/skills/`, which makes this pass and two more with subagents.
      """

  @bullet-4
  Scenario: The copy's AGENTS.md points at the write-an-issue page
    Then the copy's AGENTS.md at its root holds this text
      """
      # AGENTS.md

      The rules for an agent that reads, writes or reviews code in this repo.

      ## Layout

      | path | holds | rule |
      |---|---|---|
      | `specs/features/` | the `.feature` files | `tests/Specs/Specs.csproj` links every one of them in |
      | `tests/Specs/` | the build suite: Reqnroll step classes under `Steps/`, and xUnit unit tests under `Unit/` | each test answers every call to a service from a stub. A unit test class name ends `Tests` |
      | `tests/Integration/` | the integration suite: xUnit tests against the real services that the program calls | `dotnet run --no-restore --project tests/Integration` runs it, and `dotnet test` leaves it out |
      | `src/Core/` | the library, where the behaviour lives, and the `System.CommandLine` command tree | the tree holds every command, option, argument and action. A new option or argument is a symbol in the tree, and `--help` lists it |
      | `src/App/` | the console program, a thin shell over the library | `Program.cs` builds the Generic Host, `Microsoft.Extensions.Hosting`, and parses its arguments with the command tree from `src/Core` |

      ## How a scenario reaches the code

      Reqnroll compiles each linked feature into a test class under `obj/`, named after the feature title with `Feature` added, and one test method per scenario. A class name that ends `Feature` belongs to that generated code. A step class marked `[Binding]` answers the steps, and it calls the library in process.

      ## The integration suite

      The build suite answers each call to a service with a canned answer from a stub `HttpMessageHandler`. For each canned answer that a scenario depends on, write a test under `tests/Integration/` that gets the same answer from the real service. The test fails when the service is unreachable. A test reads each credential from the environment, by the name that the `integration` job of `.github/workflows/ci.yml` passes.

      ## Style

      `Directory.Build.props` makes every warning an error and enforces the style of `.editorconfig` in the build. `dotnet format` fixes what it can.

      - A class that stores its constructor arguments uses a primary constructor. IDE0290.
      - A test of equality or of type against a constant is a pattern: `value is null`, `value is not null`, `exitCode is not 0`, `value is string text`. A comparison of order keeps its operator: `count > 1`. IDE0041, IDE0083, IDE0078, IDE0020 and IDE0019 flag most cases. Write `is not 0` in place of `!= 0` by hand, because no analyzer flags it.

      ## Issues

      Before writing or revising an issue, read `docs/write-an-issue.md`, and file the issue only when every row of its `## Before you file` table passes.
      """
