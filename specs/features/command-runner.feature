Feature: A command runs with a timeout and returns its exit code and output

  Scenario: A program's exit code is reported
    Given a command "sh" with arguments "-c" and "exit 3"
    # sh -c 'exit 3'; echo $? prints 3
    When I run the command with a timeout of 5 seconds
    Then the command result exit code is 3

  Scenario: Standard output holds all the text the program wrote
    Given a command "sh" with arguments "-c" and "printf hello"
    # sh -c 'printf hello' prints hello
    When I run the command with a timeout of 5 seconds
    Then the standard output is "hello"

  Scenario: Standard error holds all the text and standard output holds none of it
    Given a command "sh" with arguments "-c" and "printf err 1>&2"
    # sh -c 'printf err 1>&2' prints err to standard error and nothing to standard output
    When I run the command with a timeout of 5 seconds
    Then the standard error is "err"
    And the standard output is ""

  Scenario: A megabyte to each stream arrives whole
    Given a command "sh" with arguments "-c" and "head -c 1048576 /dev/zero | tr '\0' y; head -c 1048576 /dev/zero | tr '\0' z 1>&2"
    When I run the command with a timeout of 5 seconds
    Then the standard output is 1048576 bytes of "y"
    And the standard error is 1048576 bytes of "z"

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program unchanged
    Given a command "printf" with arguments "%s" and 'a "b" $c'
    # printf %s 'a "b" $c' prints a "b" $c
    When I run the command with a timeout of 5 seconds
    Then the standard output is 'a "b" $c'

  Scenario: The program runs in the given working directory
    Given a command "sh" with arguments "-c" and 'printf "%s" "$(pwd)"' run in the working directory "/tmp"
    # sh -c 'printf "%s" "$(pwd)"' run in /tmp prints /tmp
    When I run the command with a timeout of 5 seconds
    Then the standard output is "/tmp"

  Scenario: An extra environment variable is visible to the program
    Given a command "sh" with arguments "-c" and 'printf "%s" "$FOO"' with the extra environment variable "FOO" set to "bar"
    When I run the command with a timeout of 5 seconds
    Then the standard output is "bar"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the process environment variable "FOO" is set to "old"
    And a command "sh" with arguments "-c" and 'printf "%s" "$FOO"' with the extra environment variable "FOO" set to "new"
    When I run the command with a timeout of 5 seconds
    Then the standard output is "new"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the process environment variable "FOO" is set to "old"
    And a command "sh" with arguments "-c" and 'printf "%s" "$FOO"' with no extra environment variables
    When I run the command with a timeout of 5 seconds
    Then the standard output is "old"

  Scenario: A program that outlives its timeout is stopped and reported
    Given a command "sleep" with arguments "5"
    When I run the command with a timeout of 1 seconds
    Then RunAsync throws a TimeoutException whose message contains "sleep" and "1"
    And no process running "sleep" with arguments "5" remains

  Scenario: A program that is on no PATH fails with a named error
    Given a command "this-program-does-not-exist-12345" with no arguments
    When I run the command with a timeout of 5 seconds
    Then RunAsync throws an exception whose message contains "this-program-does-not-exist-12345"

  Scenario: A caller under test uses a mocked CommandRunner and starts no program
    Given a mock of CommandRunner whose RunAsync returns a CommandResult with exit code 0, standard output "mocked-output" and standard error ""
    When a caller asks the mock to run "git" with arguments "status"
    Then the caller receives the exit code 0, the standard output "mocked-output" and the standard error ""
    And no process named "git" is started
