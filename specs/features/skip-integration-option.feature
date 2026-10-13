Feature: The option --skip-integration turns the integration suite off in the test gate

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run help lists the option
    When the program starts with the arguments "run --help"
    Then standard output holds "--skip-integration", "on or off" and "[default: off]"
    And the exit code is 0

  Scenario: feedback help lists the option
    When the program starts with the arguments "feedback --help"
    Then standard output holds "--skip-integration", "on or off" and "[default: off]"
    And the exit code is 0

  Scenario: run refuses a value that is neither on nor off
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --skip-integration maybe"
    Then standard error is the one line that holds "--skip-integration"
    And GitHub gets no request
    And the exit code is 1

  Scenario: feedback refuses a value that is neither on nor off
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --skip-integration maybe"
    Then standard error is the one line that holds "--skip-integration"
    And GitHub gets no request
    And the exit code is 1

  Scenario: run with the suite skipped runs no integration command and ends on green checks
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --skip-integration on"
    Then docker runs a command that starts with "dotnet test --no-restore --logger trx --results-directory /work/.test-gate-"
    And docker runs no command that holds "tests/Integration"
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  Scenario: feedback with the suite skipped runs no integration command
    Given GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --skip-integration on"
    Then docker runs a command that starts with "dotnet test --no-restore --logger trx --results-directory /work/.test-gate-"
    And docker runs no command that holds "tests/Integration"

  Scenario: run with the option off runs the integration command
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --skip-integration off"
    Then docker runs the command "dotnet run --no-restore --project tests/Integration" one time or more
