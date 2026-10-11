Feature: A feedback run on the pull request of a refactor issue keeps every test and runs the issue's check

  Background:
    Given the repo "factory-scratch" of the owner "sbiondolillo", the pull request 10, and an empty list of secret names
    And the build uses the model "build-model" with the turn limit 60 and the review uses the model "review-model" with the turn limit 30
    And the author of each commit is "Factory" with the email "factory@example.com"
    And GitHub answers the read of the review threads of the pull request with the threads A and B
    And GitHub answers the read of the pull request with the head ref "factory/a1b2c3d4"
    And git answers each command with the exit code 0
    And git reports a change in the work tree after each build
    And on a build prompt the agent writes the report that marks A and B fixed
    And claude answers each prompt with the error flag clear
    And each dotnet command exits with the code 0 and the TRX file holds one passed test for each scenario
    And on a review prompt the agent writes an approving review
    And GitHub answers each reply and each resolve with the status code 200 and no errors

  Scenario: A build that changes tests/Specs/Steps/GreetingSteps.cs gets a pin Redo
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor"
    And the build changes "tests/Specs/Steps/GreetingSteps.cs"
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "pin: tests/Specs/Steps/GreetingSteps.cs"
    And no review prompt runs before it

  Scenario: A build that adds the file tests/Specs/Unit/GreetingTests.cs gets a pin Redo
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor"
    And the build adds the file "tests/Specs/Unit/GreetingTests.cs"
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "pin: tests/Specs/Unit/GreetingTests.cs"
    And no review prompt runs before it

  Scenario: A build that changes tests/Integration/GreetingTests.cs gets a pin Redo
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor"
    And the build changes "tests/Integration/GreetingTests.cs"
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "pin: tests/Integration/GreetingTests.cs"
    And no review prompt runs before it

  Scenario: A build that changes specs/features/greeting.feature gets a pin Redo
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor"
    And the build changes "specs/features/greeting.feature"
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "pin: specs/features/greeting.feature"
    And no review prompt runs before it

  Scenario: A behavior issue keeps the feedback run of main
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "behavior"
    And the build changes "tests/Specs/Steps/GreetingSteps.cs"
    And each dotnet command exits with the code 0
    When a caller starts a feedback run
    Then the feedback run starts the review

  Scenario: A refactor issue with no check runs no check command
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor" and no "## Check"
    And the build changes only "src/Core/Greeter.cs"
    And each dotnet command exits with the code 0
    When a caller starts a feedback run
    Then the feedback run runs no check command
    And the feedback run starts the review

  Scenario: The check command runs under sh after the test gate passes
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor" and the line "grep -q Greeting src/Core/Greeter.cs" in the first fenced block under "## Check"
    And the check command exits with the code 0 after the build
    When a caller starts a feedback run
    Then the feedback run runs "grep -q Greeting src/Core/Greeter.cs" under sh in a copy of the work tree after the test gate passes

  Scenario: A failing check gets a Redo
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor" and the line "grep -q Greeting src/Core/Greeter.cs" under "## Check"
    And the check command exits with the code 1 after the build
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "The check command exited with the code 1"

  Scenario: A passing check starts the review
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor" and the line "grep -q Greeting src/Core/Greeter.cs" under "## Check"
    And the check command exits with the code 0 after the build
    When a caller starts a feedback run
    Then the feedback run starts the review

  Scenario: Standard output shows the check between the test and the review
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor" and a "## Check"
    And each step passes
    When a caller starts a feedback run
    Then standard output holds the lines in this order
      | line |
      | test: passed |
      | check: start |
      | check: green |
      | review: start |

  Scenario: The build prompt names the integration tests
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor"
    When a caller starts a feedback run
    Then the build prompt holds "tests/Integration/"

  Scenario: The review prompt names the integration tests
    Given GitHub answers the read of the pull request with the body "Closes #15"
    And the issue 15 has the label "refactor"
    When a caller starts a feedback run
    Then the review prompt holds "tests/Integration/"
