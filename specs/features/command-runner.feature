Feature: CommandRunner runs a program with a timeout and reports its result

  # sh -c 'exit 3'; echo $? prints 3
  Scenario: The exit code of a finished program is reported
    Given the working directory "/tmp"
    When I run "sh" with arguments "-c" "exit 3"
    Then the exit code is 3

  # sh -c 'echo hello' prints "hello\n" on standard output
  Scenario: The standard output is captured in full
    Given the working directory "/tmp"
    When I run "sh" with arguments "-c" "echo hello"
    Then the standard output is "hello\n"

  # sh -c 'echo oops 1>&2' prints "oops\n" on standard error and nothing on standard output
  Scenario: The standard error is captured, and the standard output holds none of it
    Given the working directory "/tmp"
    When I run "sh" with arguments "-c" "echo oops 1>&2"
    Then the standard error is "oops\n"
    And the standard output is ""

  # python3 -c 'print(1024*1024)' prints 1048576
  Scenario: A megabyte written to each stream arrives whole
    Given the working directory "/tmp"
    When I run "sh" with arguments "-c" "head -c 1048576 /dev/zero | tr '\0' y; head -c 1048576 /dev/zero | tr '\0' z 1>&2"
    Then the standard output is 1048576 bytes long
    And the standard error is 1048576 bytes long

  # sh -c 'echo "$1"' _ 'a "b" $c' prints the argument unchanged followed by a newline: a "b" $c
  Scenario: An argument with a space, a double quote and a dollar sign reaches the program unchanged
    Given the working directory "/tmp"
    When I run "sh" with arguments "-c" 'echo "$1"' "_" 'a "b" $c'
    Then the standard output is 'a "b" $c\n'

  # pwd, run with working directory /tmp, prints "/tmp\n"
  Scenario: The program runs in the given working directory
    Given the working directory "/tmp"
    When I run "pwd" with no arguments
    Then the standard output is "/tmp\n"

  Scenario: An extra environment variable is visible to the program with its given value
    Given the working directory "/tmp"
    And the extra environment variable "GREETING_TARGET" set to "Ada"
    When I run "sh" with arguments "-c" "echo $GREETING_TARGET"
    Then the standard output is "Ada\n"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the working directory "/tmp"
    And the environment already has "HOME" set to "/original/home"
    And the extra environment variable "HOME" set to "/overridden/home"
    When I run "sh" with arguments "-c" "echo $HOME"
    Then the standard output is "/overridden/home\n"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the working directory "/tmp"
    And the environment already has "FACTORY_TEST_INHERITED" set to "inherited-value"
    And no extra environment variables
    When I run "sh" with arguments "-c" "echo $FACTORY_TEST_INHERITED"
    Then the standard output is "inherited-value\n"

  Scenario: A program that outlives its timeout is stopped and RunAsync reports the timeout
    Given the working directory "/tmp"
    When I run "sh" with arguments "-c" "sleep 5" and a timeout of 1 second
    Then RunAsync throws a TimeoutException whose message contains "sh"
    And that TimeoutException's message contains "1"
    And the "sh" process is no longer running

  Scenario: A program on no PATH makes RunAsync throw an exception naming it
    Given the working directory "/tmp"
    When I run "this-program-does-not-exist-12345" with no arguments
    Then RunAsync throws an exception whose message contains "this-program-does-not-exist-12345"

  Scenario: A caller tested against a Moq mock of CommandRunner starts no program
    Given a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code 0, standard output "canned output" and standard error ""
    When the caller under test asks the mocked CommandRunner to run
    Then the caller's result is the canned CommandResult
    And no program was started
