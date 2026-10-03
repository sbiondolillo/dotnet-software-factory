Feature: The console program writes its argument parser's own help and error lines

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  @bullet-1
  Scenario: program help
    When the program starts with the arguments "--help"
    # run: printf '%s\n' "run" "feedback"
    Then standard output holds "run" and "feedback"
    And GitHub gets no request
    # run: printf '0'
    Then the exit code is 0

  @bullet-2
  Scenario: run help
    When the program starts with the arguments "run --help"
    # run: printf '%s\n' "--repo <repo> (REQUIRED)" "--issue <issue> (REQUIRED)" "--pass-env <pass-env>" "[default: 60]"
    Then standard output holds "--repo <repo> (REQUIRED)", "--issue <issue> (REQUIRED)", "--pass-env <pass-env>" and "[default: 60]"
    And GitHub gets no request
    # run: printf '0'
    Then the exit code is 0

  @bullet-3
  Scenario: feedback help
    When the program starts with the arguments "feedback --help"
    # run: printf '%s\n' "--pr <pr> (REQUIRED)" "[default: 60]"
    Then standard output holds "--pr <pr> (REQUIRED)" and "[default: 60]"
    And GitHub gets no request
    # run: printf '0'
    Then the exit code is 0

  @bullet-4
  Scenario: empty arguments
    When the program starts with the arguments ""
    # run: printf '%s\n' "Required command was not provided."
    Then standard error holds the line "Required command was not provided."
    # run: printf '%s\n' "run" "feedback"
    And standard output holds "run" and "feedback"
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-5
  Scenario: unknown verb
    When the program starts with the arguments "build --repo sbiondolillo/factory-scratch"
    # run: printf '%s\n' "Unrecognized command or argument 'build'."
    Then standard error holds the line "Unrecognized command or argument 'build'."
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-6
  Scenario: run without repo
    When the program starts with the arguments "run --issue 12"
    # run: printf '%s\n' "Option '--repo' is required."
    Then standard error is the one line "Option '--repo' is required."
    # run: printf '%s\n' "--issue <issue> (REQUIRED)"
    And standard output holds "--issue <issue> (REQUIRED)"
    And GitHub gets no request
    And no program runs
    # run: printf '1'
    Then the exit code is 1

  @bullet-7
  Scenario: run without issue
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch"
    # run: printf '%s\n' "Option '--issue' is required."
    Then standard error is the one line "Option '--issue' is required."
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-8
  Scenario: feedback without pr
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch"
    # run: printf '%s\n' "Option '--pr' is required."
    Then standard error is the one line "Option '--pr' is required."
    # run: printf '%s\n' "--pr <pr> (REQUIRED)"
    And standard output holds "--pr <pr> (REQUIRED)"
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-9
  Scenario: repo without owner
    When the program starts with the arguments "run --repo factory-scratch --issue 12"
    # run: printf '%s\n' --repo '<owner>/<name>'
    Then standard error is one line that holds "--repo" and "<owner>/<name>"
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-10
  Scenario: issue not a number
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue x"
    # run: printf '%s\n' "Cannot parse argument 'x' for option '--issue' as expected type 'System.Int32'."
    Then standard error is the one line "Cannot parse argument 'x' for option '--issue' as expected type 'System.Int32'."
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-11
  Scenario: issue below 1
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 0"
    # run: printf '%s\n' --issue
    Then standard error is one line that holds "--issue"
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-12
  Scenario: pr below 1
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 0"
    # run: printf '%s\n' --pr
    Then standard error is one line that holds "--pr"
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-13
  Scenario: timeout below 0
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --timeout -1"
    # run: printf '%s\n' --timeout
    Then standard error is one line that holds "--timeout"
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-14
  Scenario: timeout without value
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --timeout"
    # run: printf '%s\n' "Required argument missing for option: '--timeout'."
    Then standard error is the one line "Required argument missing for option: '--timeout'."
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-15
  Scenario: unknown option
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --bogus 2"
    # run: printf '%s\n' "Unrecognized command or argument '--bogus'."
    Then standard error holds the line "Unrecognized command or argument '--bogus'."
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1
