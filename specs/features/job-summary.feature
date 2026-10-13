Feature: A run writes a job summary with one row for each phase and the reason for each Redo

  Background:
    Given the environment variable GITHUB_STEP_SUMMARY names an empty file

  Scenario: A run with one Redo ends its summary with the phase table and the Redo list
    Given the issue has no recipe label
    And the plan answer has 7 turns and the cost "0.4213", 31 seconds after its prompt
    And the first build answer has 40 turns and the cost "1.2034", 300 seconds after its prompt
    And the second build answer has 30 turns and the cost "0.9000", 200 seconds after its prompt
    And the review answer has 12 turns and the cost "0.3100", 95 seconds after its prompt
    And the first test gate takes 120 seconds and is red with the Redo texts "dotnet test exited with the code 1" and "failed test: Specs.Unit.CheckoutTests.AddsTwoPrices: Assert.Equal() Failure: Values differ"
    And the second test gate takes 110 seconds and is green
    When the run delivers with 1 Redo
    Then the summary file ends with the lines
      """
      ## Factory run

      | phase | seconds | turns | spend | result |
      |---|---|---|---|---|
      | plan | 31 | 7 | $0.4213 | passed |
      | build 1 | 300 | 40 | $1.2034 | passed |
      | test 1 | 120 | 0 | $0 | failed |
      | build 2 | 200 | 30 | $0.9000 | passed |
      | test 2 | 110 | 0 | $0 | passed |
      | review 1 | 95 | 12 | $0.3100 | approved |

      ### Redos

      - Redo 1, after test 1: dotnet test exited with the code 1
      - Redo 1, after test 1: failed test: Specs.Unit.CheckoutTests.AddsTwoPrices: Assert.Equal() Failure: Values differ
      """

  Scenario: The seconds of a build row run to the end of the lint gate after it
    Given the issue has no recipe label
    And the first build answer comes 300 seconds after its prompt
    And the lint gate after the first build takes 15 seconds
    When the run delivers with 0 Redos
    Then the row "build 1" has the seconds 315

  Scenario: A Redo text of several lines lists its first line
    Given the issue has no recipe label
    And the first test gate is red with a Redo text of two lines, "dotnet test exited with the code 1" and "second line of the text"
    When the run delivers with 1 Redo
    Then the Redos list holds the item "- Redo 1, after test 1: dotnet test exited with the code 1"
    And the summary does not hold "second line of the text"

  Scenario: A blocking review gives its row the result blocked
    Given the issue has no recipe label
    And the first review blocks the first build with the item "The total ignores the second item of the cart."
    When the run delivers with 1 Redo
    Then the row "review 1" has the result "blocked"

  Scenario: A blocking review adds a Redo item after the review
    Given the issue has no recipe label
    And the first review blocks the first build with the item "The total ignores the second item of the cart."
    When the run delivers with 1 Redo
    Then the Redos list holds the item "- Redo 1, after review 1: The total ignores the second item of the cart."

  Scenario: A failed plan phase gives a summary of the plan row alone
    Given the issue has no recipe label
    And the plan phase fails after one prompt with 7 turns and the cost "0.4213", 31 seconds after the prompt
    When the run ends
    Then the summary file ends with the lines
      """
      ## Factory run

      | phase | seconds | turns | spend | result |
      |---|---|---|---|---|
      | plan | 31 | 7 | $0.4213 | failed |
      """

  Scenario: A plan phase with a second prompt adds the seconds, turns and spend of both
    Given the issue has no recipe label
    And the plan phase sends two prompts
    And the first plan answer has 7 turns and the cost "0.4213", 31 seconds after its prompt
    And the second plan answer has 5 turns and the cost "0.2000", 20 seconds after its prompt
    When the run delivers with 0 Redos
    Then the summary holds the row "| plan | 51 | 12 | $0.6213 | passed |"

  Scenario: A cleanup run has the rows build, test, check and review
    Given the issue has the label "cleanup"
    When the run delivers with 0 Redos
    Then the summary rows are in this order
      | row      |
      | build 1  |
      | test 1   |
      | check 1  |
      | review 1 |

  Scenario: A cleanup check that exits with the code 0 is green
    Given the issue has the label "cleanup"
    And the check exits with the code 0 after the first build
    When the run delivers with 0 Redos
    Then the row "check 1" has the result "green"

  Scenario: A cleanup check that exits with the code 1 is red
    Given the issue has the label "cleanup"
    And the check exits with the code 1 after the first build
    When the run delivers with 1 Redo
    Then the row "check 1" has the result "red"

  Scenario: A refactor run with a Check has the rows plan, build, test, check and review
    Given the issue has the label "refactor" and a "## Check"
    When the run delivers with 0 Redos
    Then the summary rows are in this order
      | row      |
      | plan     |
      | build 1  |
      | test 1   |
      | check 1  |
      | review 1 |

  Scenario: A refactor run with no Check has no check row
    Given the issue has the label "refactor" and no "## Check"
    When the run delivers with 0 Redos
    Then the summary rows are in this order
      | row      |
      | plan     |
      | build 1  |
      | test 1   |
      | review 1 |

  Scenario: A summary follows the text that the file already holds
    Given the environment variable GITHUB_STEP_SUMMARY names a file that holds the text "earlier step text"
    And the issue has no recipe label
    When the run delivers with 0 Redos
    Then the summary file starts with the text "earlier step text"
    And the summary file holds the summary after that text

  Scenario: The spend of a row rounds to four decimal places
    Given the issue has no recipe label
    And the plan answer has 4 turns and the cost "0.0560938", 77 seconds after its prompt
    When the run delivers with 0 Redos
    Then the summary holds the row "| plan | 77 | 4 | $0.0561 | passed |"
