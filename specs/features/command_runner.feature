Feature: A command runs with a timeout and returns its exit code and output

  Scenario: A program's exit code is reported
    # sh -c 'exit 3'; echo $? prints 3
    Given the program "sh" with arguments "-c" and "exit 3"
    When I run the program with CommandRunner
    Then the CommandResult exit code is 3

  Scenario: Standard output holds everything the program wrote to it
    # sh -c "printf 'hello world'" prints hello world
    Given the program "sh" with arguments "-c" and "printf 'hello world'"
    When I run the program with CommandRunner
    Then the CommandResult standard output is "hello world"

  Scenario: Standard error holds everything the program wrote to it and standard output holds none of it
    # sh -c "printf 'oops' 1>&2" prints oops to standard error and nothing to standard output
    Given the program "sh" with arguments "-c" and "printf 'oops' 1>&2"
    When I run the program with CommandRunner
    Then the CommandResult standard error is "oops"
    And the CommandResult standard output is ""

  Scenario: A megabyte to each stream arrives whole
    # sh -c "head -c 1048576 /dev/zero | tr '\0' 'a' | wc -c" prints 1048576
    # sh -c "head -c 1048576 /dev/zero | tr '\0' 'b' | wc -c" prints 1048576
    Given the program "sh" with arguments "-c" and "head -c 1048576 /dev/zero | tr '\0' 'a'; head -c 1048576 /dev/zero | tr '\0' 'b' 1>&2"
    When I run the program with CommandRunner
    Then the CommandResult standard output is 1048576 bytes of "a"
    And the CommandResult standard error is 1048576 bytes of "b"

  Scenario: An argument holding a space, a double quote and a dollar sign reaches the program as one argument
    # sh -c 'printf "%s" "$1"' sh 'a "b" $c' prints a "b" $c
    Given the program "sh" with arguments "-c", "printf "%s" "$1"", "sh" and "a "b" $c"
    When I run the program with CommandRunner
    Then the CommandResult standard output is "a "b" $c"

  Scenario: The program runs in the given working directory
    # (cd /tmp && pwd) prints /tmp
    Given the program "pwd" with no arguments
    And the working directory "/tmp"
    When I run the program with CommandRunner
    Then the CommandResult standard output, trimmed, is "/tmp"

  Scenario: An extra environment variable is visible to the program
    # FOO=bar sh -c 'printf "%s" "$FOO"' prints bar
    Given the program "sh" with arguments "-c" and "printf "%s" "$FOO""
    And the extra environment variable "FOO" set to "bar"
    When I run the program with CommandRunner
    Then the CommandResult standard output is "bar"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    # HOME=/custom sh -c 'printf "%s" "$HOME"' prints /custom
    Given the inherited environment variable "HOME" set to "/original"
    And the program "sh" with arguments "-c" and "printf "%s" "$HOME""
    And the extra environment variable "HOME" set to "/custom"
    When I run the program with CommandRunner
    Then the CommandResult standard output is "/custom"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    # HOME=/home/testuser sh -c 'printf "%s" "$HOME"' prints /home/testuser
    Given the inherited environment variable "HOME" set to "/home/testuser"
    And the program "sh" with arguments "-c" and "printf "%s" "$HOME""
    And the extra environment variable "FOO" set to "bar"
    When I run the program with CommandRunner
    Then the CommandResult standard output is "/home/testuser"

  Scenario: A program that outlives its timeout is stopped and RunAsync throws
    Given the program "sh" with arguments "-c" and "sleep 5"
    And the timeout is 00:00:00.1000000
    When I run the program with CommandRunner
    Then RunAsync throws a TimeoutException whose message names the program "sh" and the timeout 00:00:00.1000000

  Scenario: A program on no PATH makes RunAsync throw
    Given the program "no-such-program-xyz" with no arguments
    When I run the program with CommandRunner
    Then RunAsync throws an exception whose message names the program "no-such-program-xyz"
