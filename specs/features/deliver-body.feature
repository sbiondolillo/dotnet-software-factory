Feature: The pull request of a run states its scenarios, gates, Redos, factory version, spend and time

  Background:
    Given the work tree "/work" is the clone of the run "a1b2c3d4" of the repo "factory-scratch" of the owner "sbiondolillo"
    And the base branch is "main" and the issue 12 has the title "The greeter greets in French"
    And git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  Scenario: The body states the scenarios, the gates and the Redos of the run
    Given the run of the issue 12 delivers with 1 Redo
    And the plan answer has the cost "0.4213" and comes 31 seconds after its prompt
    And the first build answer has the cost "1.2034" and comes 300 seconds after its prompt
    And the second build answer has the cost "0.9000" and comes 200 seconds after its prompt
    And the review answer has the cost "0.3100" and comes 95 seconds after its prompt
    And the first test gate is red and takes 120 seconds
    And the second test gate passes and takes 110 seconds
    And at the base commit the file "specs/features/checkout.feature" holds the scenarios "Empty cart" and "Old total"
    And in the work tree the file "specs/features/checkout.feature" holds the scenarios "Total of two items" and "Empty cart", with other steps in "Empty cart"
    And the TRX files of the last test gate hold 45 passed tests
    When a caller delivers the run with the token "token"
    Then the body of the pull request starts with the text
      """
      Closes #12

      ## Scenarios

      - `specs/features/checkout.feature`: added "Total of two items"; changed "Empty cart"; removed "Old total"

      ## Gates

      - test: passed, 45 tests
      - review: approved

      Redos: 1
      """

  Scenario: The body ends with the total spend and seconds of the run
    Given the run of the issue 12 delivers with 1 Redo
    And the plan answer has the cost "0.4213" and comes 31 seconds after its prompt
    And the first build answer has the cost "1.2034" and comes 300 seconds after its prompt
    And the second build answer has the cost "0.9000" and comes 200 seconds after its prompt
    And the review answer has the cost "0.3100" and comes 95 seconds after its prompt
    And the first test gate is red and takes 120 seconds
    And the second test gate passes and takes 110 seconds
    When a caller delivers the run with the token "token"
    Then the last line of the body of the pull request is "Total: $2.8347 in 856 s"

  Scenario: A part of a second is dropped from the seconds of the total
    Given the run of the issue 12 delivers with 0 Redos
    And the plan answer has the cost "0.5000" and comes 10.9 seconds after its prompt
    And the review answer has the cost "0.2500" and comes 20.9 seconds after its prompt
    When a caller delivers the run with the token "token"
    Then the last line of the body of the pull request is "Total: $0.7500 in 31 s"

  Scenario: The console program run states its factory version before the total
    Given the console program run delivers a pull request
    When the body of the pull request is read
    Then the line before the line starting "Total:" is "Factory version: " followed by the text that the same build of the program prints for --version

  Scenario: A changed scenario keeps its title and its line says changed
    Given the run of the issue 12 delivers with 0 Redos
    And at the base commit the file "specs/features/checkout.feature" holds the scenarios "Empty cart" and "Old total"
    And in the work tree the file "specs/features/checkout.feature" holds the scenarios "Empty cart" and "Old total", with other steps in "Empty cart"
    When a caller delivers the run with the token "token"
    Then the section Scenarios of the body holds a line for "specs/features/checkout.feature" that says changed "Empty cart"

  Scenario: A scenario added to a feature file of the base commit is named after added
    Given the run of the issue 12 delivers with 0 Redos
    And at the base commit the file "specs/features/checkout.feature" holds the scenarios "Empty cart"
    And in the work tree the file "specs/features/checkout.feature" holds the scenarios "Empty cart" and "Total of two items"
    When a caller delivers the run with the token "token"
    Then the section Scenarios of the body holds a line for "specs/features/checkout.feature" that says added "Total of two items"

  Scenario: A scenario removed from a feature file is named after removed
    Given the run of the issue 12 delivers with 0 Redos
    And at the base commit the file "specs/features/checkout.feature" holds the scenarios "Empty cart" and "Old total"
    And in the work tree the file "specs/features/checkout.feature" holds the scenarios "Empty cart"
    When a caller delivers the run with the token "token"
    Then the section Scenarios of the body holds a line for "specs/features/checkout.feature" that says removed "Old total"

  Scenario: A new feature file names each of its scenarios after added
    Given the run of the issue 12 delivers with 0 Redos
    And the base commit holds no file "specs/features/payment.feature"
    And in the work tree the file "specs/features/payment.feature" holds the scenarios "Pay by card" and "Pay by invoice"
    When a caller delivers the run with the token "token"
    Then the section Scenarios of the body holds a line for "specs/features/payment.feature" that says added "Pay by card" and "Pay by invoice"

  Scenario: A removed feature file names each of its scenarios after removed
    Given the run of the issue 12 delivers with 0 Redos
    And at the base commit the file "specs/features/payment.feature" holds the scenarios "Pay by card" and "Pay by invoice"
    And the work tree holds no file "specs/features/payment.feature"
    When a caller delivers the run with the token "token"
    Then the section Scenarios of the body holds a line for "specs/features/payment.feature" that says removed "Pay by card" and "Pay by invoice"

  Scenario: A run that changes no scenario says so
    Given the run of the issue 12 delivers with 0 Redos
    And at the base commit the file "specs/features/checkout.feature" holds the scenarios "Empty cart"
    And in the work tree the file "specs/features/checkout.feature" holds the scenarios "Empty cart"
    When a caller delivers the run with the token "token"
    Then the section Scenarios of the body holds the one line "- No scenario changed."

  Scenario: A cleanup run states the check gate between the test gate and the review
    Given the run of the issue 12 delivers with 0 Redos
    And the issue 12 has the label "cleanup"
    When a caller delivers the run with the token "token"
    Then the section Gates of the body holds the line "- check: green" between the line of the test gate and the line of the review

  Scenario: A run with neither the label cleanup nor the label refactor states no check gate
    Given the run of the issue 12 delivers with 0 Redos
    When a caller delivers the run with the token "token"
    Then the section Gates of the body does not hold the line "- check: green"

  Scenario: A refactor run with a Check section states the check gate between the test gate and the review
    Given the run of the issue 12 delivers with 0 Redos
    And the issue 12 has the label "refactor" and a "## Check" section
    When a caller delivers the run with the token "token"
    Then the section Gates of the body holds the line "- check: green" between the line of the test gate and the line of the review

  Scenario: A refactor run with no Check section states no check gate
    Given the run of the issue 12 delivers with 0 Redos
    And the issue 12 has the label "refactor" and no "## Check" section
    When a caller delivers the run with the token "token"
    Then the section Gates of the body does not hold the line "- check: green"
