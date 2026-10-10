Feature: A Scope line of a cleanup issue can be a Markdown list item

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

  Scenario: A Scope line that is a dash list item covers the file
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "- README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A Scope line that is an asterisk list item covers the file
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "* README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A Scope line that is a plus list item covers the file
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "+ README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A Scope line that is a plus list item with backticks covers the file
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "+ `README.md`" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: A Scope line that is a bare path in backticks covers the file
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "`README.md`" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  Scenario: The README documents the line forms of the Scope
    Given the README of the repo
    Then the README holds the paragraph 'The body of a `cleanup` issue names the paths the build can change under the heading `## Scope`, one for each line. A line is a path, either bare or as a Markdown list item with the marker `-`, `*` or `+`, and the path can sit in backticks. A directory ends in `/`, and a path with `*` is a glob. The body holds under the heading `## Check` one fenced `sh` block, the check command, that fails before the change and exits with the code 0 after it. The factory runs the command with `sh -c` in a copy of the work tree, and a command that runs `dotnet` restores its packages itself first.'

  Scenario: A list item for a file outside the scope still gets a Redo
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "- README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "src/Core/Greeter.cs"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "outside the scope: src/Core/Greeter.cs"
    And no review prompt runs before it
