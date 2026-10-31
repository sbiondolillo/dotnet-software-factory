Feature: The option --agent runs the agent phases as Claude Code in a sandbox or as Managed Agents sessions

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run help lists the option
    When the program starts with the arguments "run --help"
    Then standard output holds "--agent", "claude-code or managed" and "[default: claude-code]"
    And the exit code is 0

  Scenario: feedback help lists the option
    When the program starts with the arguments "feedback --help"
    Then standard output holds "--agent", "claude-code or managed" and "[default: claude-code]"
    And the exit code is 0

  Scenario: run refuses a value that is neither claude-code nor managed
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent docker"
    Then standard error is the one line that holds "--agent"
    And GitHub gets no request
    And the exit code is 1

  Scenario: feedback refuses a value that is neither claude-code nor managed
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --agent docker"
    Then standard error is the one line that holds "--agent"
    And GitHub gets no request
    And the exit code is 1

  Scenario Outline: run with the managed agent needs each variable of the sessions
    Given the environment holds the variables of the managed agent
    And the environment lacks "<name>"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then standard error is the one line that holds "<name>"
    And GitHub gets no request
    And no program runs
    And the exit code is 1

    Examples:
      | name                        |
      | FACTORY_AGENT_ID            |
      | FACTORY_ENVIRONMENT_ID      |
      | FACTORY_APP_ID              |
      | FACTORY_APP_INSTALLATION_ID |
      | FACTORY_APP_PRIVATE_KEY     |

  Scenario: feedback with the managed agent needs the private key of the App
    Given the environment holds the variables of the managed agent
    And the environment lacks "FACTORY_APP_PRIVATE_KEY"
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --agent managed"
    Then standard error is the one line that holds "FACTORY_APP_PRIVATE_KEY"
    And GitHub gets no request
    And no program runs
    And the exit code is 1

  Scenario: run with the default agent needs no variable of the sessions
    Given GitHub answers each read of the check runs with a green check
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent claude-code"
    Then docker runs a command
    And the exit code is 0

  @managed
  Scenario: A managed run starts no container and writes the line of the sessions in place of the sandbox
    Given the environment holds the variables of the managed agent
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then docker runs no command
    And standard output holds exactly these lines in this order
      | line                                                    |
      | clone: start                                            |
      | sessions: start                                         |
      | plan: start                                             |
      | plan: passed                                            |
      | build: start                                            |
      | lint: start                                             |
      | test: start                                             |
      | test: passed                                            |
      | review: start                                           |
      | review: approved                                        |
      | deliver: start                                          |
      | https://github.com/sbiondolillo/factory-scratch/pull/10 |
      | watch: start                                            |
      | checks: green, unresolved threads: 0                    |
    And the exit code is 0

  @managed
  Scenario: A managed feedback run starts no container and writes the line of the sessions in place of the sandbox
    Given the environment holds the variables of the managed agent
    And GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --agent managed"
    Then docker runs no command
    And standard output holds exactly these lines in this order
      | line                                 |
      | feedback: start                      |
      | clone: start                         |
      | sessions: start                      |
      | build: start                         |
      | lint: start                          |
      | test: start                          |
      | test: passed                         |
      | review: start                        |
      | review: approved                     |
      | watch: start                         |
      | checks: green, unresolved threads: 0 |

  @managed
  Scenario: A managed run pushes the session branch before the plan prompt and deletes it after the delivery
    Given the environment holds the variables of the managed agent
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then git pushes the session branch, the name of the delivery branch with "-session" after it, before the first session starts
    And git deletes the session branch on origin after the push of the delivery branch

  @managed
  Scenario: A managed run restores the work tree on the host before the lint gate formats it
    Given the environment holds the variables of the managed agent
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then the host runs "dotnet restore --locked-mode" before "dotnet format --no-restore"

  @managed
  Scenario: The pull request body of a managed run sums the sessions and names each one
    Given the environment holds the variables of the managed agent
    And a session answer costs 112 cents for the plan, 300 cents for a build and 37 cents for a review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then the body of the pull request holds a line that starts with "Total: $4.49 in "
    And the body of the pull request ends with the line "Sessions: sesn_01 (builder, $4.12), sesn_02 (reviewer, $0.37)"

  @managed
  Scenario: The job summary of a managed run lists each session
    Given the environment holds the variables of the managed agent
    And the environment variable GITHUB_STEP_SUMMARY of the program names an empty file
    And a session answer costs 112 cents for the plan, 300 cents for a build and 37 cents for a review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then the job summary of the program ends with the lines
      """
      ### Sessions

      - sesn_01, builder, $4.12
      - sesn_02, reviewer, $0.37
      """
