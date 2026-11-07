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

  Scenario: run with the managed agent hands the backend of the run the settings of the sessions
    Given the environment holds the variables of the managed agent
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --agent managed"
    Then the backend of each run gets the settings of the sessions with the agent "agent_011dsfbuilder" and the environment "env_011dsfbuilder"
    And the exit code is 0

  Scenario: feedback with the managed agent hands the backend of the round the settings of the sessions
    Given the environment holds the variables of the managed agent
    And GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --agent managed"
    Then the backend of each run gets the settings of the sessions with the agent "agent_011dsfbuilder" and the environment "env_011dsfbuilder"

  Scenario: run with the default agent hands the backend no settings of the sessions
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the backend of each run gets no settings of the sessions

  Scenario: The program hands the backend its standard output for the progress lines and its standard error for the log
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the backend of each run writes its progress lines to standard output and its log to standard error
