Feature: Test gate

  Background:
    Given a work tree with "specs/features/checkout.feature" pinned
    And the sandbox answers every docker command from a mock

  Scenario: A passing run passes the gate
    Given a passing run
    When a caller runs the test gate
    Then the test gate gives back a passed result
    And the commands ran in the order restore, build, format check, test, integration and no other

  Scenario: The integration command alone gets the secret names
    Given a passing run
    When a caller runs the test gate with the names "API_KEY" and "SERVICE_TOKEN"
    Then the integration command ran with the names "API_KEY" and "SERVICE_TOKEN"
    And each of the other four commands ran with no name

  Scenario: A changed pin runs no command
    Given the pin finds "specs/features/checkout.feature" changed and "global.json" removed
    When a caller runs the test gate
    Then no command ran
    And the test gate gives back a failed result
    And the evidence names "specs/features/checkout.feature" and "global.json"

  Scenario: A failing restore ends the gate
    Given a passing run
    And the restore exits with a code other than 0 and writes a reason to both outputs
    When a caller runs the test gate
    Then the restore was the last command
    And the test gate gives back a failed result
    And the evidence names the restore and holds its exit code and both reasons

  Scenario: A failing build ends the gate
    Given a passing run
    And the build exits with a code other than 0 and writes a reason to both outputs
    When a caller runs the test gate
    Then the build was the last command
    And the test gate gives back a failed result
    And the evidence names the build and holds its exit code and both reasons

  Scenario: A failing format check ends the gate
    Given a passing run
    And the format check exits with a code other than 0 and writes a reason to both outputs
    When a caller runs the test gate
    Then the format check was the last command
    And the test gate gives back a failed result
    And the evidence names the format check and holds its exit code and both reasons

  Scenario: A failing test command with a passing TRX file fails the gate
    Given a passing run
    And the test command exits with the code 1 and writes a reason to both outputs
    When a caller runs the test gate
    Then the test command was the last command
    And the test gate gives back a failed result
    And the evidence names the test command and holds the code 1 and both reasons

  Scenario: Text on standard error alone does not fail the gate
    Given a passing run
    And each of the four commands writes a line to standard error
    When a caller runs the test gate
    Then the test gate gives back a passed result

  Scenario: A failed test is named in the evidence
    Given a passing run
    And the test command exits with the code 1 and writes a TRX file with the failed test "AddsTwoPrices" of the class "Specs.Unit.CheckoutTests" and the message 'Assert.Equal() Failure: Values differ'
    When a caller runs the test gate
    Then the test command was the last command
    And the test gate gives back a failed result
    And the evidence names "Specs.Unit.CheckoutTests" and "AddsTwoPrices" and holds 'Assert.Equal() Failure: Values differ'

  Scenario: A skipped test is named in the evidence
    Given a passing run
    And the test command exits with the code 0 and writes a passing TRX file that also holds the skipped test "AddsInFrench" of the class "Specs.Unit.CheckoutTests"
    When a caller runs the test gate
    Then the test command was the last command
    And the test gate gives back a failed result
    And the evidence names "Specs.Unit.CheckoutTests" and "AddsInFrench"

  Scenario: A scenario with no test in the TRX file is named in the evidence
    Given a passing run
    And the test command exits with the code 0 and writes a TRX file with only the passed test "AddsTwoPrices" of the class "Specs.Unit.CheckoutTests"
    When a caller runs the test gate
    Then the test gate gives back a failed result
    And the evidence names "specs/features/checkout.feature", "The total is the sum of the prices" and "TheTotalIsTheSumOfThePrices"

  Scenario: A test with a passed and a failed result fails the gate
    Given a passing run
    And the test command exits with the code 1 and writes a TRX file with two results of the test "TheTotalIsTheSumOfThePrices" of the class "Specs.Features.CheckoutFeature", one passed and one failed
    When a caller runs the test gate
    Then the test gate gives back a failed result
    And the evidence names "TheTotalIsTheSumOfThePrices"

  Scenario: A TRX file of an earlier run does not fail this run
    Given a passing run
    And the work tree holds the TRX file of an earlier run with a failed test
    And the test command of this run exits with the code 0 and writes a passing TRX file
    When a caller runs the test gate
    Then the test gate gives back a passed result

  Scenario: A TRX file of an earlier run does not hide a test command that wrote none
    Given a passing run
    And the work tree holds the TRX file of an earlier run with a passed test
    And the test command of this run exits with the code 1, writes a reason to standard output and writes no TRX file
    When a caller runs the test gate
    Then the test gate gives back a failed result
    And the evidence is one text
    And the evidence names the test command and holds the code 1 and the reason

  Scenario: A failing integration command fails the gate
    Given a passing run
    And the integration command exits with the code 1 and writes a summary to standard output
    When a caller runs the test gate
    Then the test gate gives back a failed result
    And the evidence names the integration command and holds the code 1 and the summary

  Scenario: A gate that skips the integration suite runs no integration command
    Given a passing run
    When a caller runs the test gate that skips the integration suite
    Then the test gate gives back a passed result
    And the commands ran in the order restore, build, format check, test and no other
