Feature: The program run exits with the code 0 on an issue that it declines

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario Outline: Two labels that exclude each other end the program with the exit code 0
    Given the issue 12 has the label "<first>"
    And the issue 12 has the label "<second>"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the program posts one comment on the issue 12
    And the exit code is 0

    Examples:
      | first    | second   |
      | cleanup  | reframe  |
      | behavior | reframe  |
      | refactor | reframe  |
      | behavior | refactor |
      | cleanup  | refactor |
      | behavior | cleanup  |

  Scenario Outline: A body with no heading ## Scope ends the program with the exit code 0
    Given the issue 12 has the label "<label>" and this body:
      """
      Fix the install test.
      """
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the program posts one comment on the issue 12
    And the exit code is 0

    Examples:
      | label   |
      | cleanup |
      | reframe |

  Scenario Outline: A body with no heading ## Check ends the program with the exit code 0
    Given the issue 12 has the label "<label>" and this body:
      """
      ## Scope
      README.md
      """
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the program posts one comment on the issue 12
    And the exit code is 0

    Examples:
      | label   |
      | cleanup |
      | reframe |

  Scenario: A body whose heading ## Scenarios holds no fenced block ends the program with the exit code 0
    Given the issue 12 has no label and this body:
      """
      ## Scenarios

      The scenarios follow.
      """
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the program posts one comment on the issue 12
    And the exit code is 0
