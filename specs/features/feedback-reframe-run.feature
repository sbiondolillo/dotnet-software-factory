Feature: A feedback run on the pull request of a reframe issue keeps that issue's scope, its tests and its check

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

  Scenario: A reframe change outside the scope gets a Redo
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe" and the scope "README.md"
    And the build changes only "src/Core/Greeter.cs"
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "outside the scope: src/Core/Greeter.cs"

  Scenario: A test that the reframe loses gets a Redo
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe"
    And the test "Specs.Unit.GreeterTests.Greets" passed on the head of the pull request
    And the TRX file after the build holds no test of the class "Specs.Unit.GreeterTests" and the method "Greets"
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "lost test: " and the name "Specs.Unit.GreeterTests.Greets"

  Scenario: A failing check of a reframe gets a Redo
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe" and the line "grep -q Feedback README.md" under "## Check"
    And the check command exits with the code 1 after the build
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "The check command exited with the code 1"

  Scenario: A passing check of a reframe starts the review
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe" and the line "grep -q Feedback README.md" under "## Check"
    And the check command exits with the code 0 after the build
    When a caller starts a feedback run
    Then the feedback run runs "grep -q Feedback README.md" under sh in a copy of the work tree after the test gate passes
    And the feedback run starts the review

  Scenario: The build prompt of a reframe feedback run disputes a thread outside the scope
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe" and the scope "README.md"
    When a caller starts a feedback run
    Then the build prompt holds "A thread that asks for a change to a path outside the `## Scope` takes the status `disputed`"

  Scenario: A feedback run on a declined issue stops with the decline text
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the labels "cleanup" and "refactor"
    When a caller starts a feedback run
    Then FeedbackRun posts one comment on the pull request 10 that holds "The labels refactor and cleanup exclude each other"
    And FeedbackRun runs 0 build prompts in the run

  Scenario: A reframe round runs the API gate after the test gate
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe" and the scope "README.md"
    When a caller starts a feedback run
    Then the job summary holds the row "api 1" with the result "same"
    And FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos

  Scenario: A public API that the reframe round changes gets a Redo
    Given GitHub lists the issue 11 as the closing issue of the pull request
    And the issue 11 has the label "reframe" and the scope "README.md"
    And the API printer lists "Core.Greeter.Hello()" on the base commit and lists nothing after the first build
    When a caller starts a feedback run
    Then the next build prompt holds a Redo text that holds "lost symbol: Core.Greeter.Hello()"
    And the Redos list of the job summary holds the item "- Redo 1, after api 1: lost symbol: Core.Greeter.Hello()"
