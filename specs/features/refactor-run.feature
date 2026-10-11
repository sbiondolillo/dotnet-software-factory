Feature: A refactor run changes the structure of the code and leaves every test as it is

  Background:
    Given the repo "factory-scratch" of the owner "sbiondolillo", the issue 12 and the token "token"
    And the run id "a1b2c3d4", the base branch "main" and an empty list of secret names
    And the plan model "plan-model" with turn limit 20, the build model "build-model" with turn limit 60 and the review model "review-model" with turn limit 30
    And the author name "Factory" and the author email "factory@example.com"
    And the clone holds the four paths of the layout and its head commit is "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And git reports a change in the work tree after each build
    And claude answers each prompt with the error flag clear
    And each dotnet command exits with the code 0 and the TRX file holds one passed test
    And the agent approves on the review prompt
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  Scenario: The labels refactor and behavior get a comment
    Given the issue has the labels "refactor" and "behavior"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "refactor" and "behavior"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: The labels refactor and cleanup get a comment
    Given the issue has the labels "refactor" and "cleanup"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "refactor" and "cleanup"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A body with no Check block runs no check and one plan prompt
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with no heading "## Check"
    When a caller starts a run
    Then the container runs no "sh -c" command
    And FactoryRun runs one prompt with the model "plan-model"

  Scenario: The check command runs under sh before the plan prompt
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    When a caller starts a run
    Then the container runs "sh -c" with the argument "test -f src/Core/Greeting.cs" before the plan prompt

  Scenario: The check command runs in a detached worktree outside /work
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    When a caller starts a run
    Then the container runs "git worktree add --detach" at a path outside "/work" and the check command runs in that path on the head commit

  Scenario: The copy is removed when the check command ends
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    When a caller starts a run
    Then the container runs "git worktree remove --force" on the copy after the check command ends

  Scenario: A check that passes before the plan gets a comment
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 0 before the plan
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "The check passes before the plan"
    And FactoryRun runs no plan prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A check that exits with 126 gets a comment with its standard error
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 126 before the plan and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "126" and the standard error of the command
    And FactoryRun runs no plan prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A check that exits with 127 gets a comment with its standard error
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 127 before the plan and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "127" and the standard error of the command
    And FactoryRun runs no plan prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A red check starts the plan prompt with the issue
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "plan-model" and the turn limit 20 that holds the title of the issue, the body of the issue and ".factory/plan.md"

  Scenario: The refactor plan prompt names no feature directory
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    When a caller starts a run
    Then the plan prompt holds no "specs/features/"

  Scenario: A plan that writes only the plan file starts the build prompt
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "build-model" and the turn limit 60 that holds the title of the issue, the body of the issue and ".factory/plan.md"

  Scenario: A plan that also writes specs/features/checkout.feature gets a correction prompt naming it
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the first plan prompt writes ".factory/plan.md" and the file "specs/features/checkout.feature"
    When a caller starts a run
    Then the second plan prompt holds "specs/features/checkout.feature"

  Scenario: A plan that also writes src/Core/Extra.cs gets a correction prompt naming it
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the first plan prompt writes ".factory/plan.md" and the file "src/Core/Extra.cs"
    When a caller starts a run
    Then the second plan prompt holds "src/Core/Extra.cs"

  Scenario: A missing plan gets a comment and no build
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on each plan prompt leaves ".factory/plan.md" absent
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds ".factory/plan.md"
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: The refactor build prompt names no unit test directory
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    When a caller starts a run
    Then the build prompt holds no "tests/Specs/Unit/"

  Scenario: A changed step file gets a pin Redo
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    And the build changes only "tests/Specs/Steps/GreetingSteps.cs"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "pin: tests/Specs/Steps/GreetingSteps.cs"
    And no review prompt runs before it

  Scenario: A changed integration project file gets a pin Redo
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    And the build changes only "tests/Integration/Integration.csproj"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "pin: tests/Integration/Integration.csproj"
    And no review prompt runs before it

  Scenario: A changed feature file gets a pin Redo
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    And the build changes only "specs/features/greeting.feature"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "pin: specs/features/greeting.feature"
    And no review prompt runs before it

  Scenario: A new unit test file gets a pin Redo
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    And the build adds only the file "tests/Specs/Unit/GreetingTests.cs"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "pin: tests/Specs/Unit/GreetingTests.cs"
    And no review prompt runs before it

  Scenario: A removed step file gets a pin Redo
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the agent on the plan prompt writes ".factory/plan.md" and changes no other file
    And the build removes only the file "tests/Specs/Steps/GreetingSteps.cs"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "pin: tests/Specs/Steps/GreetingSteps.cs"
    And no review prompt runs before it

  Scenario: A build that changes only source files reaches the review
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with no heading "## Check"
    And the build changes only "src/Core/Greeter.cs" and "src/Core/Greeting.cs"
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A red check after a build gets a Redo with its output
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the check command exits with the code 1 after the build and writes a standard output and a standard error
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "1", the standard output and the standard error of the command
    And no review prompt runs before it

  Scenario: A green check after a build starts the review prompt with the plan
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "review-model" and the turn limit 30 that holds the title of the issue, the body of the issue, the base commit and ".factory/plan.md"

  Scenario: An approved refactor build opens a pull request
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun opens a pull request with the title "Rename Greeting" and gives back a result with that pull request and 0 Redos

  Scenario: Four red builds give a comment and no pull request
    Given the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the check command exits with the code 1 before the plan
    And the check command exits with the code 1 after each of four builds and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds the standard error of the command
    And FactoryRun pushes no branch
    And FactoryRun gives back a result with no pull request and 3 Redos

  Scenario: The program writes one progress line for each refactor step with a check
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the issue 12 has the label "refactor" and a "## Check" and each step passes
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds exactly these lines in this order
      | line |
      | clone: start |
      | sandbox: start |
      | check: start |
      | check: red |
      | plan: start |
      | plan: passed |
      | build: start |
      | lint: start |
      | test: start |
      | test: passed |
      | check: start |
      | check: green |
      | review: start |
      | review: approved |
      | deliver: start |
      | https://github.com/sbiondolillo/factory-scratch/pull/10 |
      | watch: start |
      | checks: green, unresolved threads: 0 |

  Scenario: The program writes one progress line for each refactor step with no check
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the issue 12 has the label "refactor" and no "## Check" and each step passes
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds exactly these lines in this order
      | line |
      | clone: start |
      | sandbox: start |
      | plan: start |
      | plan: passed |
      | build: start |
      | lint: start |
      | test: start |
      | test: passed |
      | review: start |
      | review: approved |
      | deliver: start |
      | https://github.com/sbiondolillo/factory-scratch/pull/10 |
      | watch: start |
      | checks: green, unresolved threads: 0 |
