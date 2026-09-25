Feature: A command runs with a timeout and returns its exit code and output

  Scenario: A program's exit code is captured
    Given the program "sh"
    And the arguments
      | -c |
      | exit 3 |
    When I run the command
    # sh -c 'exit 3'; echo $?
    # 3
    Then the result's exit code is 3

  Scenario: Standard output is captured whole
    Given the program "sh"
    And the arguments
      | -c |
      | printf hello-out |
    When I run the command
    Then the standard output is "hello-out"

  Scenario: Standard error is captured separately from standard output
    Given the program "sh"
    And the arguments
      | -c |
      | printf hello-err 1>&2 |
    When I run the command
    Then the standard error is "hello-err"
    And the standard output is ""

  Scenario: A megabyte on each stream arrives whole
    Given the program "sh"
    And the arguments
      | -c |
      | head -c 1048576 /dev/zero \| tr '\0' y 1>&2; head -c 1048576 /dev/zero \| tr '\0' x |
    When I run the command
    # head -c 1048576 /dev/zero | tr '\0' x | wc -c
    # 1048576
    Then the standard output is 1048576 bytes of "x"
    # head -c 1048576 /dev/zero | tr '\0' y | wc -c
    # 1048576
    And the standard error is 1048576 bytes of "y"

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program unchanged
    Given the program "sh"
    And the arguments
      | -c |
      | printf %s "$1" |
      | sh |
      | a b"c$d |
    When I run the command
    Then the standard output is 'a b"c$d'

  Scenario: The command runs in the given working directory
    Given the program "sh"
    And the arguments
      | -c |
      | pwd |
    And the working directory "/tmp"
    When I run the command
    Then the standard output contains "/tmp"

  Scenario: An extra environment variable is visible to the program
    Given the program "sh"
    And the arguments
      | -c |
      | echo $FACTORY_TEST_VAR |
    And the extra environment variable "FACTORY_TEST_VAR" set to "extra-value"
    When I run the command
    Then the standard output contains "extra-value"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the current process has an environment variable named "FACTORY_TEST_VAR" set to "inherited-value"
    And the program "sh"
    And the arguments
      | -c |
      | echo $FACTORY_TEST_VAR |
    And the extra environment variable "FACTORY_TEST_VAR" set to "override-value"
    When I run the command
    Then the standard output contains "override-value"
    And the standard output does not contain "inherited-value"

  Scenario: An inherited variable left out of the extra variables keeps its inherited value
    Given the current process has an environment variable named "FACTORY_TEST_VAR" set to "inherited-value"
    And the program "sh"
    And the arguments
      | -c |
      | echo $FACTORY_TEST_VAR |
    When I run the command
    Then the standard output contains "inherited-value"

  Scenario: A program that outlasts its timeout is stopped and raises a TimeoutException
    Given the program "sh"
    And the arguments
      | -c |
      | sleep 5 |
    And the timeout is 1 second
    When I run the command
    Then running the command raises a "TimeoutException"
    And the exception message contains "sh"
    And the exception message contains "1"

  Scenario: A program on no PATH raises an exception naming the program
    Given the program "definitely-not-a-real-program-xyz" with no arguments
    When I run the command
    Then running the command raises an exception
    And the exception message contains "definitely-not-a-real-program-xyz"

  Scenario: A caller tested against a mocked CommandRunner starts no program
    Given a Moq mock of "CommandRunner" whose "RunAsync" returns a canned result with exit code 0 and standard output "canned-output"
    When the caller under test runs a command through the mock
    Then the caller sees the canned result with standard output "canned-output"
    And no program is started
