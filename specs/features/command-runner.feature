Feature: CommandRunner runs a program with a timeout and returns its exit code and output

  Scenario: A program's exit code is reported
    Given the program "sh" with arguments "-c" and "exit 3"
    When I run the program with CommandRunner
    # sh -c 'exit 3'; echo $? -> 3
    Then the result's exit code is 3

  Scenario: Standard output is captured whole
    Given the program "sh" with arguments "-c" and "printf \"hello world\""
    When I run the program with CommandRunner
    # sh -c 'printf "hello world"' -> hello world
    Then the result's standard output is "hello world"

  Scenario: Standard error is captured separately from standard output
    Given the program "sh" with arguments "-c" and 'printf "OUT"; printf "ERR" 1>&2'
    When I run the program with CommandRunner
    # sh -c 'printf "OUT"; printf "ERR" 1>&2' -> stdout OUT, stderr ERR
    Then the result's standard output is "OUT"
    And the result's standard error is "ERR"
    And the result's standard output does not contain "ERR"

  Scenario: A megabyte on each stream arrives whole
    Given the program "sh" with arguments "-c" and "head -c 1048576 /dev/zero | tr '\0' y; head -c 1048576 /dev/zero | tr '\0' y 1>&2"
    When I run the program with CommandRunner
    # head -c 1048576 /dev/zero | tr '\0' y | wc -c -> 1048576
    Then the result's standard output is exactly 1048576 bytes of "y"
    And the result's standard error is exactly 1048576 bytes of "y"

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program whole
    Given the program "sh" with arguments "-c", "printf '%s' \"$1\"" and '_' and 'a "b" $c'
    When I run the program with CommandRunner
    # sh -c 'printf "%s" "$1"' _ 'a "b" $c' -> a "b" $c
    Then the result's standard output is 'a "b" $c'

  Scenario: The program runs in the given working directory
    Given the program "pwd" with no arguments
    And the working directory is "/tmp"
    When I run the program with CommandRunner
    Then the result's standard output, trimmed, is "/tmp"

  Scenario: An extra environment variable is visible to the program
    Given the program "sh" with arguments "-c" and "printf '%s' \"$FACTORY_TEST_VAR\""
    And an extra environment variable "FACTORY_TEST_VAR" set to "extra-value"
    When I run the program with CommandRunner
    Then the result's standard output is "extra-value"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the program "sh" with arguments "-c" and "printf '%s' \"$FACTORY_TEST_VAR\""
    And an inherited environment variable "FACTORY_TEST_VAR" set to "inherited-value"
    And an extra environment variable "FACTORY_TEST_VAR" set to "overriding-value"
    When I run the program with CommandRunner
    Then the result's standard output is "overriding-value"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the program "sh" with arguments "-c" and "printf '%s' \"$FACTORY_TEST_VAR\""
    And an inherited environment variable "FACTORY_TEST_VAR" set to "inherited-value"
    When I run the program with CommandRunner
    Then the result's standard output is "inherited-value"

  Scenario: A program that outlives its timeout is stopped and reported
    Given the program "sh" with arguments "-c" and "sleep 5"
    And a timeout of 1 second
    When I run the program with CommandRunner
    Then RunAsync throws a TimeoutException whose message names "sh" and "1 second"
    And the program is stopped

  Scenario: A program on no PATH fails to start
    Given the program "no-such-factory-program-xyz"
    When I run the program with CommandRunner
    Then RunAsync throws an exception whose message names "no-such-factory-program-xyz"

  Scenario: A caller tested against a mocked CommandRunner starts no program
    Given a caller that uses a Moq mock of CommandRunner
    And the mock's RunAsync returns a canned CommandResult with exit code 0, standard output "canned-output" and standard error ""
    When the caller runs
    Then the caller reports success with "canned-output"
    And no program starts
