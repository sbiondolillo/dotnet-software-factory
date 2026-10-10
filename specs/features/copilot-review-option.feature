Feature: The option --copilot-review turns the Copilot code review request off

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run help lists the option
    When the program starts with the arguments "run --help"
    Then standard output holds "--copilot-review", "off" and "[default: on]"
    And the exit code is 0

  Scenario: feedback help lists the option
    When the program starts with the arguments "feedback --help"
    Then standard output holds "--copilot-review", "off" and "[default: on]"
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

  Scenario: run with the review on requests the review
    Given GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  Scenario: feedback with the review on requests the review
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  Scenario: The README states the review option in its Copilot paragraph
    When a reader reads the section "## Start a new repo" of README.md
    Then the section holds the line 'The factory requests a Copilot code review of each pull request it watches, and waits for that review on the head commit. This needs a Copilot plan that includes Copilot code review. With no such plan, add `--copilot-review off` to the run command in `factory.yml` and in `review-feedback.yml`. The factory then requests no review and waits for the checks alone. The review prompt of the factory reviews each build with either value.'
