Feature: The test gate runs the CI commands and every pinned scenario passes

  @bullet-1
  Scenario: A passing run passes and runs the five commands in order
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And a passing run
    When a caller runs the test gate
    Then the result is passed
    And the Sandbox ran these commands in order: the restore, the build, the format check, the test, the integration command
    And the Sandbox ran no other command

  @bullet-2
  Scenario: The integration command alone gets the names of the secrets
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And a passing run
    When a caller runs the test gate with the secret names "API_KEY" and "SERVICE_TOKEN"
    Then the integration command ran with both names
    And the restore, the build, the format check and the test ran with no name

  @bullet-3
  Scenario: A pin that finds a change runs no command
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds "specs/features/checkout.feature" changed and "global.json" removed
    When a caller runs the test gate
    Then the Sandbox ran no command
    And the result is failed
    # run: printf 'specs/features/checkout.feature\nglobal.json'
    And the evidence names "specs/features/checkout.feature" and "global.json"

  @bullet-4
  Scenario: A restore that exits with a code other than 0 ends the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the restore exits with the code 2 and writes a reason to standard output and a reason to standard error
    When a caller runs the test gate
    Then the restore was the last command the Sandbox ran
    And the result is failed
    And the evidence names the restore and holds its exit code and both reasons

  @bullet-5
  Scenario: A build that exits with a code other than 0 ends the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the build exits with the code 2 and writes a reason to standard output and a reason to standard error
    When a caller runs the test gate
    Then the build was the last command the Sandbox ran
    And the result is failed
    And the evidence names the build and holds its exit code and both reasons

  @bullet-6
  Scenario: A format check that exits with a code other than 0 ends the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the format check exits with the code 2 and writes a reason to standard output and a reason to standard error
    When a caller runs the test gate
    Then the format check was the last command the Sandbox ran
    And the result is failed
    And the evidence names the format check and holds its exit code and both reasons

  @bullet-7
  Scenario: A failing test command with a passing TRX file still fails the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the test command exits with the code 1 and writes a reason to standard output and a reason to standard error
    And the test command writes a passing TRX file
    When a caller runs the test gate
    Then the test command was the last command the Sandbox ran
    And the result is failed
    # run: printf '1'
    And the evidence names the test command and holds the code 1 and both reasons

  @bullet-8
  Scenario: Standard error lines with exit code 0 do not fail the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And a passing run
    And each of the four commands writes a line to standard error
    When a caller runs the test gate
    Then the result is passed

  @bullet-9
  Scenario: A failed test puts its class, method and message in the evidence
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the test command exits with the code 1 and writes a TRX file that holds the failed test "AddsTwoPrices" of the class "Specs.Unit.CheckoutTests" with the message "Assert.Equal() Failure: Values differ"
    When a caller runs the test gate
    Then the test command was the last command the Sandbox ran
    And the result is failed
    # run: printf 'Specs.Unit.CheckoutTests\nAddsTwoPrices\nAssert.Equal() Failure: Values differ'
    And the evidence names "Specs.Unit.CheckoutTests" and "AddsTwoPrices" and holds "Assert.Equal() Failure: Values differ"

  @bullet-10
  Scenario: A skipped test fails the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the test command exits with the code 0 and writes a passing TRX file that also holds the skipped test "AddsInFrench" of the class "Specs.Unit.CheckoutTests"
    When a caller runs the test gate
    Then the test command was the last command the Sandbox ran
    And the result is failed
    # run: printf 'Specs.Unit.CheckoutTests\nAddsInFrench'
    And the evidence names "Specs.Unit.CheckoutTests" and "AddsInFrench"

  @bullet-11
  Scenario: A scenario with no test in the TRX file fails the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the test command exits with the code 0 and writes a TRX file that holds the passed test "AddsTwoPrices" of the class "Specs.Unit.CheckoutTests" and no test named "TheTotalIsTheSumOfThePrices"
    When a caller runs the test gate
    Then the result is failed
    # run: printf 'specs/features/checkout.feature\nThe total is the sum of the prices\nTheTotalIsTheSumOfThePrices'
    And the evidence names "specs/features/checkout.feature", "The total is the sum of the prices" and "TheTotalIsTheSumOfThePrices"

  @bullet-12
  Scenario: One failed result of a test fails the gate though another passed
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the test command exits with the code 1 and writes a TRX file that holds two results of the test "TheTotalIsTheSumOfThePrices" of the class "Specs.Features.CheckoutFeature", one passed and one failed
    When a caller runs the test gate
    Then the result is failed
    # run: printf 'TheTotalIsTheSumOfThePrices'
    And the evidence names "TheTotalIsTheSumOfThePrices"

  @bullet-13
  Scenario: A TRX file of an earlier run does not count in this run
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And a passing run
    And the work tree holds the TRX file of an earlier run of the gate with a failed test
    When a caller runs the test gate
    Then the result is passed

  @bullet-14
  Scenario: A test command that writes no TRX file leaves one text of evidence
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the earlier commands exit with the code 0
    And the work tree holds the TRX file of an earlier run of the gate with a passed test
    And the test command exits with the code 1 and writes a reason to standard output and writes no TRX file
    When a caller runs the test gate
    Then the result is failed
    And the evidence is one text
    # run: printf '1'
    And the evidence names the test command and holds the code 1 and the reason

  @bullet-15
  Scenario: A failing integration command fails the gate with its summary
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" with the scenario "The total is the sum of the prices", pinned before the gate runs
    And the pin finds no change
    And the four commands exit with the code 0 and the test command writes a passing TRX file
    And the integration command exits with the code 1 and writes a summary to standard output
    When a caller runs the test gate
    Then the result is failed
    # run: printf '1'
    And the evidence names the integration command and holds the code 1 and the summary

