Feature: The log of a run shows each call to claude and each gate command with its exit code

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: The plan start line comes before the plan prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then when claude gets the plan prompt standard error already holds the line "agent: plan start"

  Scenario: The plan end line shows turns, cost and seconds
    Given claude answers the plan prompt with 7 turns and the cost 0.4213 after 31 seconds
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "agent: plan end, 7 turns, $0.4213, 31 s"

  Scenario: The build start line comes before the build prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then when claude gets a build prompt standard error already holds the line "agent: build start"

  Scenario: The build end line shows turns, cost and seconds
    Given claude answers a build prompt with 40 turns and the cost 1.2034 after 300 seconds
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "agent: build end, 40 turns, $1.2034, 300 s"

  Scenario: The review start line comes before the review prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then when claude gets a review prompt standard error already holds the line "agent: review start"

  Scenario: The review end line shows turns, cost and seconds
    Given claude answers a review prompt with 12 turns and the cost 0.3100 after 95 seconds
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "agent: review end, 12 turns, $0.3100, 95 s"

  Scenario: A part of a second is dropped from the seconds
    Given claude answers the plan prompt with 7 turns and the cost 0.4213 after 31.9 seconds
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the end line of the plan prompt ends with ", 31 s"

  Scenario: Each dotnet command of the gates has a line with its exit code
    Given every dotnet command in the container exits with the code 0
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds these command lines in this order
      | line                                                                                        |
      | command: dotnet format --no-restore exited 0                                                |
      | command: dotnet restore --locked-mode exited 0                                              |
      | command: dotnet build --no-restore exited 0                                                 |
      | command: dotnet format --verify-no-changes --no-restore exited 0                            |
      | command: dotnet test --no-restore --logger trx --results-directory <any> exited 0           |
      | command: dotnet run --no-restore --project tests/Integration exited 0                       |

  Scenario: A failed build command shows exit code 1
    Given the dotnet command "dotnet build --no-restore" exits with the code 1
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "command: dotnet build --no-restore exited 1"

  Scenario: A failing check of a cleanup issue shows exit code 1
    Given the issue has the label "cleanup" and the check exits with the code 1
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "command: check exited 1"

  Scenario: A passing check of a cleanup issue shows exit code 0
    Given the issue has the label "cleanup" and the check exits with the code 0
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "command: check exited 0"

  Scenario: A failing check of a refactor issue shows exit code 1
    Given the issue has the label "refactor" and a "## Check" and the check exits with the code 1
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds the line "command: check exited 1"
