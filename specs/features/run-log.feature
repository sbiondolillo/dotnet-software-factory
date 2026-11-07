Feature: The log of a run shows each gate command with its exit code

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

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

  Scenario: Each dotnet command of the gates of a feedback round has a line with its exit code
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    And every dotnet command in the container exits with the code 0
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then standard error holds these command lines in this order
      | line                                                                                        |
      | command: dotnet format --no-restore exited 0                                                |
      | command: dotnet restore --locked-mode exited 0                                              |
      | command: dotnet build --no-restore exited 0                                                 |
      | command: dotnet format --verify-no-changes --no-restore exited 0                            |
      | command: dotnet test --no-restore --logger trx --results-directory <any> exited 0           |
      | command: dotnet run --no-restore --project tests/Integration exited 0                       |
