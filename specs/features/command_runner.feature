Feature: A command runs with a timeout and returns its exit code and output

  CommandRunner is the one place in the library that starts a program. It
  runs a program with its arguments, a working directory, extra environment
  variables and a timeout, and returns a CommandResult holding the exit
  code, the standard output and the standard error.

  Scenario: A program's exit code is returned
    # sh -c 'exit 3'; echo $?
    # prints: 3
    Given the program "sh" with arguments "-c" "exit 3"
    When I run the command with a timeout of 5 seconds
    Then the result's exit code is 3

  Scenario: Standard output is captured in full
    # sh -c "printf 'hello there'"
    # prints: hello there
    Given the program "sh" with arguments "-c" "printf 'hello there'"
    When I run the command with a timeout of 5 seconds
    Then the result's standard output is "hello there"

  Scenario: Standard error is captured separately from standard output
    # sh -c "printf 'err text' 1>&2"
    # prints (stderr): err text
    Given the program "sh" with arguments "-c" "printf 'err text' 1>&2"
    When I run the command with a timeout of 5 seconds
    Then the result's standard error is "err text"
    And the result's standard output is ""

  Scenario: Large output on both streams arrives whole
    # head -c 1048576 /dev/zero | tr '\0' y | wc -c
    # prints: 1048576
    # head -c 1048576 /dev/zero | tr '\0' z | wc -c
    # prints: 1048576
    Given the program "sh" with arguments "-c" "head -c 1048576 /dev/zero | tr '\0' y; head -c 1048576 /dev/zero | tr '\0' z 1>&2"
    When I run the command with a timeout of 30 seconds
    Then the result's standard output is 1048576 bytes of "y"
    And the result's standard error is 1048576 bytes of "z"

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program intact
    # sh -c 'printf "%s" "$1"' sh 'a "b" $c'
    # prints: a "b" $c
    Given the program "sh" is run with these arguments
      | argument                    |
      | -c                          |
      | printf '%s' "$1"            |
      | sh                          |
      | a "b" $c                    |
    When I run the command with a timeout of 5 seconds
    Then the result's standard output is 'a "b" $c'

  Scenario: The program runs in the given working directory
    # cd /tmp && sh -c pwd
    # prints: /tmp
    Given the program "sh" with arguments "-c" "pwd"
    And the working directory "/tmp"
    When I run the command with a timeout of 5 seconds
    Then the result's standard output contains "/tmp"

  Scenario: An extra environment variable is visible to the program
    # FOO=bar sh -c 'printf "%s" "$FOO"'
    # prints: bar
    Given the program "sh" with arguments "-c" "printf \"%s\" \"$FOO\""
    And the extra environment variable "FOO" set to "bar"
    When I run the command with a timeout of 5 seconds
    Then the result's standard output is "bar"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    # env -i FOO=new sh -c 'printf "%s" "$FOO"'
    # prints: new
    Given the inherited environment variable "FOO" is set to "old"
    And the program "sh" with arguments "-c" "printf \"%s\" \"$FOO\""
    And the extra environment variable "FOO" set to "new"
    When I run the command with a timeout of 5 seconds
    Then the result's standard output is "new"

  Scenario: An inherited variable not named in the extra variables keeps its inherited value
    # env -i BAZ=kept sh -c 'printf "%s" "$BAZ"'
    # prints: kept
    Given the inherited environment variable "BAZ" is set to "kept"
    And the program "sh" with arguments "-c" "printf \"%s\" \"$BAZ\""
    When I run the command with a timeout of 5 seconds
    Then the result's standard output is "kept"

  Scenario: A program still running past its timeout is stopped and reported
    Given the program "sh" with arguments "-c" "sleep 5"
    When I run the command with a timeout of 1 seconds and it times out
    Then RunAsync throws a TimeoutException whose message names the program "sh" and the timeout of 1 seconds

  Scenario: A program that is on no PATH makes RunAsync throw
    # ./this-program-does-not-exist-12345
    # prints (stderr): No such file or directory, exit code 127
    Given the program "this-program-does-not-exist-12345" with no arguments
    When I run the command with a timeout of 5 seconds and it fails to start
    Then RunAsync throws an exception whose message names the program "this-program-does-not-exist-12345"

  Scenario: A caller tested against a mocked CommandRunner uses the canned result and starts no program
    Given a Moq mock of CommandRunner whose RunAsync returns exit code 0, standard output "canned output" and standard error ""
    When the caller runs its command through the mocked CommandRunner
    Then the caller's result matches the canned result
    And no program is started
