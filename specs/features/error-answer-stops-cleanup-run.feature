Feature: A cleanup run stops at the first answer of claude that carries the error flag

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

  @bullet-4
  Scenario: An error answer to the first build prompt of a cleanup issue ends the run at the build prompt
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And claude answers the first build prompt with the error flag set
    When a caller starts a run
    Then the build prompt is the last docker exec command of the run

  @bullet-5
  Scenario: An error answer to the first review prompt of a cleanup issue ends the run at the review prompt
    Given the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And claude answers the first review prompt with the error flag set
    When a caller starts a run
    Then the review prompt is the last docker exec command of the run
