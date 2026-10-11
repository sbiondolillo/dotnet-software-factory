Feature: The option --copilot-review turns the Copilot code review request on

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run help lists the option
    When the program starts with the arguments "run --help"
    Then standard output holds "--copilot-review", "on or off" and "[default: off]"
    And the exit code is 0

  Scenario: feedback help lists the option
    When the program starts with the arguments "feedback --help"
    Then standard output holds "--copilot-review", "on or off" and "[default: off]"
    And the exit code is 0

  Scenario: run refuses a value that is neither on nor off
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review maybe"
    Then standard error is the one line that holds "--copilot-review"
    And GitHub gets no request
    And the exit code is 1

  Scenario: feedback refuses a value that is neither on nor off
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --copilot-review maybe"
    Then standard error is the one line that holds "--copilot-review"
    And GitHub gets no request
    And the exit code is 1

  Scenario: run with the review off ends on green checks
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review off"
    Then GitHub gets no review request
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  Scenario: feedback with the review off ends on green checks
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --copilot-review off"
    Then GitHub gets no review request
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  Scenario: run with the review off waits for a check run in progress
    Given GitHub answers the first read of the check runs with the check run "build" with the status "in_progress" and no conclusion
    And GitHub answers each later read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review off"
    Then standard output holds the line "checks: green, unresolved threads: 0"

  Scenario: run with the review off still runs the review prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review off"
    Then claude gets the review prompt
    And standard error holds the line "agent: review start"

  Scenario: run with no review option requests no review and ends on green checks
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then GitHub gets no review request
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  Scenario: feedback with no review option requests no review and ends on green checks
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then GitHub gets no review request
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0
