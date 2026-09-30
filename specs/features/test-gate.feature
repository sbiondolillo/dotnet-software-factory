Feature: The test gate runs the CI commands and every pinned scenario passes

  @bullet-1
  Scenario: A passing run passes and runs the five commands in order
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    When a caller runs the test gate with no secret names
    Then the test gate result is passed
    And the Sandbox ran the restore, the build, the format check, the test and the integration command in that order
    And the Sandbox ran no other command

  @bullet-2
  Scenario: Secret names go to the integration command only
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    When a caller runs the test gate with the names "API_KEY" and "SERVICE_TOKEN"
    # run: printf 'API_KEY\nSERVICE_TOKEN'
    Then the integration command ran with the names "API_KEY" and "SERVICE_TOKEN"
    And each of the four other commands ran with no name

  @bullet-3
  Scenario: A pin that finds a change runs no command
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the pin finds "specs/features/checkout.feature" changed and "global.json" removed
    When a caller runs the test gate with no secret names
    Then the Sandbox ran no command
    And the test gate result is failed
    # run: printf 'specs/features/checkout.feature\nglobal.json'
    And the evidence names "specs/features/checkout.feature" and "global.json"

  @bullet-4
  Scenario Outline: A command that exits with a code other than 0 is the last command
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the <command> command exits with a code other than 0 and writes a reason to standard output and a reason to standard error
    When a caller runs the test gate with no secret names
    Then the <command> command was the last command the Sandbox ran
    And the test gate result is failed
    And the evidence names the <command> command and holds its exit code and both reasons

    Examples:
      | command     |
      | restore     |
      | build       |
      | format check|
      | integration |

  @bullet-5
  Scenario: Standard error output alone does not fail the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And each of the four commands writes a line to standard error
    When a caller runs the test gate with no secret names
    Then the test gate result is passed

  @bullet-6
  Scenario: A failed test puts its class, method and message in the evidence
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the test command exits with the code 1 and writes a TRX file that holds the failed test "AddsTwoPrices" of the class "Specs.Unit.CheckoutTests" with the message "Assert.Equal() Failure: Values differ"
    When a caller runs the test gate with no secret names
    Then the test command was the last command the Sandbox ran
    And the test gate result is failed
    # run: printf 'Specs.Unit.CheckoutTests\nAddsTwoPrices\nAssert.Equal() Failure: Values differ'
    And the evidence names "Specs.Unit.CheckoutTests" and "AddsTwoPrices" and holds "Assert.Equal() Failure: Values differ"

  @bullet-7
  Scenario: A skipped test puts its class and method in the evidence
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the test command exits with the code 0 and writes a passing TRX file that also holds the skipped test "AddsInFrench" of the class "Specs.Unit.CheckoutTests"
    When a caller runs the test gate with no secret names
    Then the test command was the last command the Sandbox ran
    And the test gate result is failed
    # run: printf 'Specs.Unit.CheckoutTests\nAddsInFrench'
    And the evidence names "Specs.Unit.CheckoutTests" and "AddsInFrench"

  @bullet-8
  Scenario: A scenario with no test in the TRX file puts its feature file, title and test name in the evidence
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the test command exits with the code 0 and writes a TRX file that holds the passed test "AddsTwoPrices" of the class "Specs.Unit.CheckoutTests" and no test named "TheTotalIsTheSumOfThePrices"
    When a caller runs the test gate with no secret names
    Then the test gate result is failed
    # run: printf 'specs/features/checkout.feature\nThe total is the sum of the prices\nTheTotalIsTheSumOfThePrices'
    And the evidence names "specs/features/checkout.feature", "The total is the sum of the prices" and "TheTotalIsTheSumOfThePrices"

  @bullet-9
  Scenario: A test with one passed and one failed result fails the gate
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the test command exits with the code 1 and writes a TRX file that holds two results of the test "TheTotalIsTheSumOfThePrices" of the class "Specs.Features.CheckoutFeature", one passed and one failed
    When a caller runs the test gate with no secret names
    Then the test gate result is failed
    # run: printf 'TheTotalIsTheSumOfThePrices'
    And the evidence names "TheTotalIsTheSumOfThePrices"

  @bullet-10
  Scenario: A TRX file of an earlier run with a failed test is ignored when this run passes
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the work tree holds the TRX file of an earlier run of the gate with a failed test
    When a caller runs the test gate with no secret names
    Then the test gate result is passed

  @bullet-11
  Scenario: A TRX file of an earlier run is not read when this run writes none
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the work tree holds the TRX file of an earlier run of the gate with a passed test
    And the test command exits with the code 1, writes a reason to standard output and writes no TRX file
    When a caller runs the test gate with no secret names
    Then the test gate result is failed
    # run: printf '1'
    And the evidence is one text, which names the test command and holds the code 1 and the reason

  @bullet-12
  Scenario: A failed integration command puts its code and summary in the evidence
    Given a TestGate built over a Sandbox over a mock CommandRunner
    And the work tree holds "specs/features/checkout.feature" pinned before the gate runs
    And the run is a passing run
    And the integration command exits with the code 1 and writes a summary to standard output
    When a caller runs the test gate with no secret names
    Then the test gate result is failed
    # run: printf '1'
    And the evidence names the integration command and holds the code 1 and the summary
