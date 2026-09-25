Feature: A test of a caller runs against a mock of CommandRunner

  Code that calls CommandRunner tests itself against a Moq mock of the runner,
  so that no test of a caller starts a program. The caller receives the canned
  CommandResult the mock was given, and no program is started.

  Scenario: A caller given a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult passes with that canned result and starts no program
    Given the caller holds a Moq mock of CommandRunner
    Given the mock's RunAsync returns a CommandResult with exit code 0, standard output "canned output" and empty standard error
    When the caller asks the runner to run sh with the argument --version
    Then the caller receives exactly the canned CommandResult
    And no program is started

  Scenario: A caller given a Moq mock whose RunAsync returns a failing CommandResult passes with that canned result
    Given the caller holds a Moq mock of CommandRunner
    Given the mock's RunAsync returns a CommandResult with exit code 2, standard error "canned error" and empty standard output
    When the caller asks the runner to run sh with the argument --version
    Then the caller receives exactly the canned CommandResult
    And no program is started
