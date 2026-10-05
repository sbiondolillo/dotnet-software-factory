Feature: A scope line of a cleanup issue can be a Markdown list item

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

  @bullet-1
  Scenario: The scope line - README.md for README.md covers it (form 1)
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "- README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  @bullet-2
  Scenario: The scope line * README.md for README.md covers it (form 2)
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "* README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  @bullet-3
  Scenario: The scope line + README.md for README.md covers it (form 3)
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "+ README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  @bullet-4
  Scenario: The scope line + README.md for README.md covers it (form 4)
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "+ `README.md`" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  @bullet-5
  Scenario: The scope line README.md for README.md covers it (form 5)
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "`README.md`" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "README.md"
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt

  @bullet-6
  Scenario: The README states the forms of a scope line
    When a reader reads the section "## Start a new repo" of README.md
    # run: printf '%s\n' '-' '*' '+' '`'
    Then the section states that a scope line is a path, bare or as a Markdown list item with the marker "-", "*" or "+", and that the path can sit in backticks "`"
