Feature: A feedback run on the pull request of a cleanup issue keeps that issue's scope and check

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

  @bullet-1
  Scenario: A change outside the scope gets a Redo
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup" and the scope "README.md"
    And the build changes only "src/Core/Greeter.cs"
    When a caller starts a feedback run
    # run: printf '%s' 'outside the scope: src/Core/Greeter.cs'
    Then the next build prompt holds a Redo text that holds "outside the scope: src/Core/Greeter.cs"

  @bullet-2
  Scenario: A change to a build settings file that the scope names passes the test gate
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup" and the scope ".editorconfig"
    And the build changes only ".editorconfig"
    When a caller starts a feedback run
    Then the test gate of the feedback run passes

  @bullet-3
  Scenario: A lost test gets a Redo
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup"
    And the test "Specs.Unit.GreeterTests.Greets" passed on the head of the pull request
    And the TRX file after the build holds no test of the class "Specs.Unit.GreeterTests" and the method "Greets"
    When a caller starts a feedback run
    # run: printf '%s\n' 'lost test: ' 'Specs.Unit.GreeterTests.Greets'
    Then the next build prompt holds a Redo text that holds "lost test: " and the name "Specs.Unit.GreeterTests.Greets"

  @bullet-4
  Scenario: The check command runs under sh after the test gate passes
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup" and the first fenced block under "## Check" holding the line "grep -q Feedback README.md"
    And the check command exits with the code 0 after the build
    When a caller starts a feedback run
    # run: printf '%s' 'grep -q Feedback README.md'
    Then the feedback run runs "grep -q Feedback README.md" under sh in a copy of the work tree after the test gate passes

  @bullet-5
  Scenario: A failing check gets a Redo
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup" and the line "grep -q Feedback README.md" under "## Check"
    And the check command exits with the code 1 after the build
    When a caller starts a feedback run
    # run: printf '%s' 'The check command exited with the code 1'
    Then the next build prompt holds a Redo text that holds "The check command exited with the code 1"

  @bullet-6
  Scenario: A passing check starts the review
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup" and the line "grep -q Feedback README.md" under "## Check"
    And the check command exits with the code 0 after the build
    When a caller starts a feedback run
    Then the feedback run starts the review

  @bullet-7
  Scenario: Standard output shows the check between the test and the review
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has the label "cleanup" and the line "grep -q Feedback README.md" under "## Check"
    And the check command exits with the code 0 after the build
    When a caller starts a feedback run
    # run: printf '%s\n' 'test: passed' 'check: start' 'check: green' 'review: start'
    Then standard output holds the lines in this order
      | line |
      | test: passed |
      | check: start |
      | check: green |
      | review: start |

  @bullet-8
  Scenario: An issue with no label cleanup gets no check
    Given GitHub answers the read of the pull request with the body "Closes #11"
    And the issue 11 has no label "cleanup" and the line "grep -q Feedback README.md" under "## Check"
    When a caller starts a feedback run
    Then the feedback run runs no check command

  @bullet-9
  Scenario: A body with no closing line reads no issue
    Given GitHub answers the read of the pull request with a body that holds no closing line
    When a caller starts a feedback run
    Then the feedback run reads no issue
    And the feedback run runs no check command

  @bullet-10
  Scenario: The README documents the feedback run of a cleanup pull request
    When a reader reads the section "## Start a new repo" of README.md
    # run: printf '%s' 'The body of a `cleanup` issue names the paths'
    Then the section holds a sentence after the paragraph that starts 'The body of a `cleanup` issue names the paths'
    And the sentence states that a feedback run on the pull request of a `cleanup` issue holds the build to the issue's `## Scope` and runs its `## Check`
