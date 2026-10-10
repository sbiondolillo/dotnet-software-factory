Feature: The console program writes its argument parser's own help and error lines

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: program help
    When the program starts with the arguments "--help"
    Then standard output holds "run" and "feedback"
    And GitHub gets no request
    Then the exit code is 0

  Scenario: run help
    When the program starts with the arguments "run --help"
    Then standard output holds "--repo <repo> (REQUIRED)", "--issue <issue> (REQUIRED)", "--pass-env <pass-env>" and "[default: 60]"
    And GitHub gets no request
    Then the exit code is 0

  Scenario: feedback help
    When the program starts with the arguments "feedback --help"
    Then standard output holds "--pr <pr> (REQUIRED)" and "[default: 60]"
    And GitHub gets no request
    Then the exit code is 0

  Scenario: empty arguments
    When the program starts with the arguments ""
    Then standard error holds the line "Required command was not provided."
    And standard output holds "run" and "feedback"
    And GitHub gets no request
    Then the exit code is 1

  Scenario: unknown verb
    When the program starts with the arguments "build --repo sbiondolillo/factory-scratch"
    Then standard error holds the line "Unrecognized command or argument 'build'."
    And GitHub gets no request
    Then the exit code is 1

  Scenario: run without repo
    When the program starts with the arguments "run --issue 12"
    Then standard error is the one line "Option '--repo' is required."
    And standard output holds "--issue <issue> (REQUIRED)"
    And GitHub gets no request
    And no program runs
    Then the exit code is 1

  Scenario: run without issue
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch"
    Then standard error is the one line "Option '--issue' is required."
    And GitHub gets no request
    Then the exit code is 1

  Scenario: feedback without pr
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch"
    Then standard error is the one line "Option '--pr' is required."
    And standard output holds "--pr <pr> (REQUIRED)"
    And GitHub gets no request
    Then the exit code is 1

  Scenario: repo without owner
    When the program starts with the arguments "run --repo factory-scratch --issue 12"
    Then standard error is one line that holds "--repo" and "<owner>/<name>"
    And GitHub gets no request
    Then the exit code is 1

  Scenario: issue not a number
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue x"
    Then standard error is the one line "Cannot parse argument 'x' for option '--issue' as expected type 'System.Int32'."
    And GitHub gets no request
    Then the exit code is 1

  Scenario: issue below 1
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 0"
    Then standard error is one line that holds "--issue"
    And GitHub gets no request
    Then the exit code is 1

  Scenario: pr below 1
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 0"
    Then standard error is one line that holds "--pr"
    And GitHub gets no request
    Then the exit code is 1

  Scenario: timeout below 0
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --timeout -1"
    Then standard error is one line that holds "--timeout"
    And GitHub gets no request
    Then the exit code is 1

  Scenario: timeout without value
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --timeout"
    Then standard error is the one line "Required argument missing for option: '--timeout'."
    And GitHub gets no request
    Then the exit code is 1

  Scenario: unknown option
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --bogus 2"
    Then standard error holds the line "Unrecognized command or argument '--bogus'."
    And GitHub gets no request
    Then the exit code is 1
