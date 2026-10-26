Feature: A cleanup run fixes a test, a config file or a document

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

  Scenario: A cleanup run runs no plan prompt
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then FactoryRun runs no prompt with the plan model

  Scenario: The labels cleanup and behavior get a comment
    Given the issue has the labels "cleanup" and "behavior"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "cleanup" and "behavior"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A body with no Scope heading gets a comment
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with no heading "## Scope"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "## Scope"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A Scope heading with only blank lines gets a comment
    Given the issue has the label "cleanup", the title "Fix the install test" and a body whose heading "## Scope" holds only blank lines before the next heading
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "## Scope"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A body with no Check heading gets a comment
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with no heading "## Check"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "## Check"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A Check heading with no fenced block gets a comment
    Given the issue has the label "cleanup", the title "Fix the install test" and a body whose heading "## Check" holds no fenced block before the next heading
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "## Check"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: The check command runs under sh before the build prompt
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then the container runs "sh -c" with the argument "grep -q Install README.md" before the build prompt

  Scenario: The check command runs in a detached worktree outside /work
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then the container runs "git worktree add --detach" at a path outside "/work" and the check command runs in that path on the head commit

  Scenario: The copy is removed when the check command ends
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then the container runs "git worktree remove --force" on the copy after the check command ends

  Scenario: A check that passes before the build gets a comment
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 0 before the build
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "The check passes before the build"
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A check that exits with 126 gets a comment with its standard error
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 126 before the build and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "126" and the standard error of the command
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A check that exits with 127 gets a comment with its standard error
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 127 before the build and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "127" and the standard error of the command
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A red check starts the build prompt with the issue
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "build-model" and the turn limit 60 that holds the title and the body of the issue

  Scenario: The cleanup build prompt names no plan
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then the build prompt holds no ".factory/plan.md"

  Scenario: A change outside the scope gets a Redo
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "src/Core/Greeter.cs"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "outside the scope: src/Core/Greeter.cs"
    And no review prompt runs before it

  Scenario: A directory entry covers the paths below it
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "tests/Specs/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "tests/Specs/Steps/GreetingSteps.cs"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A glob entry covers the matching paths
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "tests/Specs/Steps/*Steps.cs" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "tests/Specs/Steps/GreetingSteps.cs"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A file entry covers that file
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A file entry does not cover a file of the same name elsewhere
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "docs/README.md"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "outside the scope: docs/README.md"

  Scenario: A change to a feature file gets a pin Redo
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "specs/features/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "specs/features/greeting.feature"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "pin: specs/features/greeting.feature"
    And no review prompt runs before it

  Scenario: An entry for a settings file covers it
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope ".editorconfig" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only ".editorconfig"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A lost test gets a Redo
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the test "Specs.Unit.GreeterTests.Greets" passed on the base commit
    And the TRX file after the build holds no test of the class "Specs.Unit.GreeterTests" and the method "Greets"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "lost test: Specs.Unit.GreeterTests.Greets"
    And no review prompt runs before it

  Scenario: A new passed test with no lost test reaches the review
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the TRX file after the build holds a passed test that the base commit lacks
    And each test that passed on the base commit passes
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A red check after a build gets a Redo with its output
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command exits with the code 1 after the build and writes a standard output and a standard error
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "1", the standard output and the standard error of the command
    And no review prompt runs before it

  Scenario: A green check after a build starts the review prompt
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "review-model" and the turn limit 30 that holds the title of the issue, the body of the issue and the base commit

  Scenario: The cleanup review prompt names no plan
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command then exits with the code 0
    When a caller starts a run
    Then the review prompt holds no ".factory/plan.md"

  Scenario: An approved cleanup build opens a pull request
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun opens a pull request with the title "Fix the install test" and gives back a result with that pull request and 0 Redos

  Scenario: Four red builds give a comment and no pull request
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command exits with the code 1 after each of four builds and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds the standard error of the command
    And FactoryRun pushes no branch
    And FactoryRun gives back a result with no pull request and 3 Redos

  Scenario: The program writes one progress line for each cleanup step
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the issue 12 has the label "cleanup" and each step passes
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds exactly these lines in this order
      | line |
      | clone: start |
      | sandbox: start |
      | check: start |
      | check: red |
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

  Scenario: A redo line sits between the red check and the next build
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the issue 12 has the label "cleanup"
    And the check command exits with the code 1 after the first build and with the code 0 after the second
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds the lines in this order with no line between them
      | line |
      | check: red |
      | redo: 1 |
      | build: start |

  Scenario: The cleanup review prompt asks for no integration test
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command then exits with the code 0
    When a caller starts a run
    Then the review prompt holds no "For each canned answer"
