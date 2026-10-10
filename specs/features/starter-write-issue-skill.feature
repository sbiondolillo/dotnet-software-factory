Feature: A repo made from the starter holds the skill write-issue

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  Scenario: The skill write-issue proves the check of a draft before the judges read it
    Then the copy's file ".claude/skills/write-issue/SKILL.md" holds the line "name: write-issue"
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds a line that holds "description: Write an issue that the factory builds, judge the draft, and file it."
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds the line "## 4. Prove the check"
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds the line "A draft with no `## Check` skips this step. Run the check of the draft in a copy of `main`, once for each run of the table of [`PROOF.md`](PROOF.md) that fits the check, and write each exit status to a proof file. A run that gives another status than its row asks for is a fault of the check: fix the check, and run the whole table again. Done when each run gives the status its row asks for, on one `main` SHA."
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds the line "## 5. Judge the draft"
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds a line that holds "A fix to the check runs step 4 again, and each hole becomes a run of its table."

  Scenario: The copy's REVIEW.md holds the angles that judge a draft
    Then the copy's file ".claude/skills/write-issue/REVIEW.md" holds the line "verdict: accept | reject"
    And the copy's file ".claude/skills/write-issue/REVIEW.md" holds a line that holds "**Body rules.**"
    And the copy's file ".claude/skills/write-issue/REVIEW.md" holds a line that holds "**Claims against `main`.**"
    And the copy's file ".claude/skills/write-issue/REVIEW.md" holds a line that holds "**Testability.**"
    And the copy's file ".claude/skills/write-issue/REVIEW.md" holds a line that holds "**Holes.** For a draft with `## Check`."

  Scenario: The copy's PROOF.md holds the proof of a check
    Then the copy's file ".claude/skills/write-issue/PROOF.md" holds this text, with a line that holds "## A check that raises an analyzer rule"
      """
      # Prove a check

      Reference for step 4 of [`write-issue`](SKILL.md), and for the angle `Holes` of [`REVIEW.md`](REVIEW.md). The proof runs the check of the draft in a copy of `main`, once for each run of a table, and reads the exit status of each run. The rules of a check are `## A check` of `docs/write-an-issue.md`.

      ## The copy

      The factory runs the check with `sh -c` in a `git worktree` of the change, with the network on. A worktree holds the tracked files only. The proof uses the same shape. `<PROOF>`, `<CHECK>` and `<HOME>` are absolute paths outside the repo.

      Save the check of the draft to `<CHECK>`. This program reads the first fenced block under `## Check` as the factory reads it:

      ```
      awk '{ sub(/\r$/, ""); t = $0; sub(/^[ \t]+/, "", t); sub(/[ \t]+$/, "", t) }
      m != "" { if (t ~ ("^" substr(m, 1, 1) "+$") && length(t) >= length(m)) { if (s) { if (c ~ /[^ \t\n]/) printf "%s", c; exit } m = "" } else if (s) c = c $0 "\n"; next }
      t ~ /^#+[ \t]/ && t !~ /^#######/ { if (s) exit; s = (t == "## Check"); next }
      match(t, /^(```+|~~~+)/) { m = substr(t, 1, RLENGTH); if (m ~ /^`/ && index(substr(t, RLENGTH + 1), "`")) m = "" }' <draft> > <CHECK>
      ```

      Read `<CHECK>`: it holds the lines of the block and nothing else. An empty `<CHECK>` tells that the factory reads no check: fix the block of the draft. Then make the home directory and the copy:

      ```
      mkdir -p <HOME> && git fetch -q origin && git worktree add --detach <PROOF> origin/main
      ```

      A `docker run` mounts a missing path as a directory that `root` owns. The restore is then refused, or the run prints nothing and exits with the code 0. So `<CHECK>` and `<HOME>` exist before the first run.

      Reset the copy to the `main` SHA before each run:

      ```
      git -C <PROOF> reset -q --hard <main SHA> && git -C <PROOF> clean -qfdx
      ```

      Then make the change of the row. A change that runs `dotnet`, such as `dotnet format`, runs in the image:

      ```
      docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp/home -v <HOME>:/tmp/home -v <PROOF>:<PROOF> -w <PROOF> mcr.microsoft.com/dotnet/sdk:<major.minor> dotnet format
      ```

      It leaves `obj/` and `bin/`, and the factory's copy has none. Remove them with `git -C <PROOF> clean -qfdX`, then plant the file of the row, if it has one.

      Run the check in the SDK image of `global.json`: the tag is its major and minor version, such as `10.0`. The run mounts the git directory of the repo and the copy at their own paths, so `git` in the copy reads the repo. Each run uses the same `<HOME>`, so a restore reads the package cache of the run before it.

      ```
      docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp/home -v <HOME>:/tmp/home -v "$(git rev-parse --path-format=absolute --git-common-dir):$(git rev-parse --path-format=absolute --git-common-dir)" -v <PROOF>:<PROOF> -v <CHECK>:/check.sh:ro -w <PROOF> mcr.microsoft.com/dotnet/sdk:<major.minor> sh -x /check.sh; echo "exit $?"
      ```

      The trace of `sh -x` ends at the command that stopped the run. Read it for each run that must fail. Remove the copy when the table is complete:

      ```
      git worktree remove --force <PROOF>
      ```

      ## The runs

      | Run | The change in the copy | Must exit |
      |---|---|---|
      | `main` | none | not 0, and not 126 or 127, at the first line that reads the change |
      | faked | the cheapest change that meets the prose: a move by cut and paste with no fix of a `using`, or the smallest edit of a test that meets the prose. The tests can fail | 0 |
      | other means, one for each choice | the faked change with another helper, another form of an assertion or another form of a count, for each choice that the prose leaves to the build | 0 |
      | partial, one for each element | the faked change minus one element, as bullet 1 of `## A check` lists them | not 0, at the line of that element |

      The exit codes 126 and 127 tell that the check cannot run: fix the check. A partial run that exits with the code 0 is a hole: add a line to the check, and run the table again. An "other means" run that exits with a code other than 0 tells that the check reads a means: take that line out of the check.

      ## A check that raises an analyzer rule

      The check has the parts of `docs/write-an-issue.md` → `## A check`, and its proof takes this table in place of the table above:

      | Run | The change in the copy | Must exit |
      |---|---|---|
      | `main` | none | not 0, at the `sha256sum -c` |
      | before | the settings files as the prose states them, and no code changed | not 0, at the first count of part 7, or at the build when the check has no count |
      | faked | before, with each finding fixed by `dotnet format` or by a script | 0 |
      | hole, one for each form | faked, with one form planted: each hiding form of part 5, one generated name of part 6 in another case, such as `Greeter.Designer.cs`, one new settings file of part 4, such as `Directory.Build.rsp`, and one `*.props` file under an `obj/` of part 1 | not 0, at the line of its part |
      | dodge, one for each pinned line | faked, with the pinned line rewritten to dodge the rule, and a comment that pads its count | not 0, at the line pin |

      A build stops at the first project that fails, so the projects that depend on it give no findings. To list every finding of every project, run `dotnet build -p:TreatWarningsAsErrors=false` once in the copy with the change of the "before" row. The "before" run itself runs the check as written.

      ## The proof file

      The proof file is the comment that step 6 posts. It names the full `main` SHA and the image, and holds one row for each run:

      | Run | The change | Exit | Stops at |
      |---|---|---|---|
      | `main` | none | 1 | `sha256sum -c` |

      Done when each run of the table gives the status that its row asks for, on one `main` SHA.
      """

  Scenario: The skill directory holds SKILL.md, REVIEW.md and PROOF.md and no other file
    Then the copy's directory ".claude/skills/write-issue" holds the files "SKILL.md", "REVIEW.md" and "PROOF.md" and no other file

  Scenario: The write-an-issue page holds the rules of a check
    Then the copy's file "docs/write-an-issue.md" holds this text, with a line that holds "## A check"
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
      """

  Scenario: The example body of the write-an-issue page applies to the copy
    Given the body in the markdown block of the copy's "docs/write-an-issue.md"
    When the scenarios of that body are applied to the copy
    Then the apply gives no fault
    And the copy holds "specs/features/farewell.feature"
    And the copy's "specs/features/greeting.feature" ends with the scenario "An empty name gets the greeting of the world"

  Scenario: The awk program of PROOF.md saves the check of a body in the form of the template
    Given the draft body
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Scope

      - `tests/Specs/Steps/GreetingSteps.cs`

      ## Check

      ```sh
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves the check of a body with CRLF line ends
    Given the draft body, with CRLF line ends
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Scope

      - `tests/Specs/Steps/GreetingSteps.cs`

      ## Check

      ```sh
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves a check from an indented tilde fence
    Given the draft body
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Check

        ~~~sh
        dotnet restore --locked-mode &&
        dotnet build --no-restore
        ~~~
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
        dotnet restore --locked-mode &&
        dotnet build --no-restore
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves a check whose fence holds a shorter fence and a heading line
    Given the draft body
      """
      Reframe. Raise IDE0022.

      ## Check

      ````sh
      ## 1. No obj and no bin
      test -z "$(find . \( -name obj -o -name bin \) -print)" &&
      cat > notes.md <<'EOF' &&
      ```
      EOF
      printf '%s\n' '```' | grep -qx '```'
      ````
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      ## 1. No obj and no bin
      test -z "$(find . \( -name obj -o -name bin \) -print)" &&
      cat > notes.md <<'EOF' &&
      ```
      EOF
      printf '%s\n' '```' | grep -qx '```'
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves the check under the heading, past a fenced example that holds the heading
    Given the draft body
      """
      Cleanup. The page shows the form of a check:

      ~~~markdown
      ## Check

      ```sh
      false
      ```
      ~~~

      ## Check

      ```sh
      true
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      true
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check from a body whose fence is not closed
    Given the draft body
      """
      Refactor. Move the class Greeter to src/Core/Greeting.

      ## Check

      ```sh
      test -f src/Core/Greeting/Greeter.cs
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves a check past a trailing space, a tab, a longer closing fence and a backtick info string
    Given the draft body, with each "<sp>" as a space and each "<tab>" as a tab
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Check<sp>

      ```sh `not a fence`
      <tab>~~~sh
      dotnet build --no-restore
      <tab>~~~~<sp>
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet build --no-restore
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check from a blank block
    Given the draft body
      """
      ## Check

      ```sh

      ```

      ```sh
      true
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check when the first Check heading holds no block
    Given the draft body
      """
      ## Check

      No block here.

      ## Check

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves the first block under the heading and no later block
    Given the draft body
      """
      ## Check

      ```sh
      true
      ```

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      true
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check from a block under a subheading
    Given the draft body
      """
      ## Check

      ### Note

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check under a heading other than ## Check
    Given the draft body
      """
      ## Checklist

      ```sh
      false
      ```

      ### Check

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md reads a line that starts with a hash and is no heading as text
    Given the draft body
      """
      ## Check

      #242 holds the reason for this check.

      ####### x

      ```sh
      true
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      true
      """
    And CleanupBody reads the same check from that body

  Scenario: The Holes angle of the skill write-issue refuses a pin of the program, and names when it accepts
    Then the copy's file ".claude/skills/write-issue/REVIEW.md" holds the line "**Holes.** For a draft with `## Check`. Find each way a change exits with the code 0 on the check and misses a part of what the prose asks for: an element with no line of its own, a means that the check reads, a form that hides code from an analyzer, a count that a comment pads. A check pins the claim of the story, not the program. A break that the build or the test suite catches is not a hole. `Holes` accepts when each remaining hole needs a change that no part of the prose implies. Run each likely hole in a copy of `main` as [`PROOF.md`](PROOF.md) says, and give its exit status in the finding. Done when each likely hole has run, and each hole that exits with the code 0 is a finding whose `fix` holds the line of the check that closes it, or, for a hole that needs a change no part of the prose implies, a line in the proof file and no finding."

  Scenario: The skill write-issue stops the judges of a draft after round 2
    Then the copy's file ".claude/skills/write-issue/SKILL.md" holds the line "Run the angles of [`REVIEW.md`](REVIEW.md) on the file, each as a subagent, in parallel: `Body rules`, `Claims against main` and `Testability`, and `Holes` too for a draft with `## Check`. A harness with no subagents runs them one after another in this session. Apply every fix of the verdicts in one pass. A fix to the check runs step 4 again, and each hole becomes a run of its table. This is round 1. When a verdict of round 1 is `reject`, run the angles again on the fixed draft. This is round 2, and the last round. Apply the fixes of round 2 the same way, except for a hole. A hole of round 2 goes into the proof file of step 4 with its command and its exit status, and gets no fix: a draft with `## Check` has a recipe label, so it has no `## Context`, and step 6 posts the proof file as a comment. No round 3 runs, and the person is not asked for one. Done when each verdict of the last round is `accept`, or each of its findings is fixed or, for a hole of round 2, in the proof file."
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds no line that holds "A line that two rounds reject goes to the person"
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds no line that holds "Then run the angles again."
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds a line that holds "An issue with `## Check` gets the proof file of step 4 as a comment:"
    And the copy's file ".claude/skills/write-issue/SKILL.md" holds the line "gh issue comment <n> --body-file <proof file>"
