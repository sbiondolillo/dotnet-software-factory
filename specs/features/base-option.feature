Feature: The option --base names the base commit of a run
  The repair loop starts the factory at the last green ref of the factory, and the change still lands on the head of
  the default branch. So the job resolves the ref of its input base to a commit, and the run builds on that commit.

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run help lists the option
    When the program starts with the arguments "run --help"
    Then standard output holds "--base", "full SHA" and "default branch"
    And the exit code is 0

  Scenario Outline: run refuses a base that is not the full SHA of a commit
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --base <base>"
    Then standard error is the one line that holds "--base"
    And GitHub gets no request
    And the exit code is 1

    Examples:
      | base                                      |
      | main                                      |
      | 9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0   |
      | 9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c0 |
      | 9B8C7D6E5F4A3B2C1D0E9F8A7B6C5D4E3F2A1B0C  |

  Scenario: run with a base checks that commit out in the clone before it reads the head of the clone
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --base 9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c"
    Then git checks out the commit "9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c" detached in the clone, after the clone and before the first head read

  Scenario: run with no base checks out no commit
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then git checks out no commit detached
