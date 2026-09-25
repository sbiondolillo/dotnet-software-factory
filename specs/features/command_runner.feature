Feature: A command runs with a timeout and returns its exit code and output

  Scenario: The exit code of the finished program is reported
    Given the program "sh" with arguments:
      | -c     |
      | exit 3 |
    When I run the command
    # sh -c "exit 3"; echo $? prints 3
    Then the result exit code is 3

  Scenario: Standard output is captured whole
    Given the program "sh" with arguments:
      | -c                  |
      | printf 'hello there' |
    When I run the command
    # sh -c "printf 'hello there'" prints hello there
    Then the result standard output is "hello there"

  Scenario: Standard error is captured and kept out of standard output
    Given the program "sh" with arguments:
      | -c                                    |
      | printf out 1>&1; printf err 1>&2      |
    When I run the command
    # sh -c "printf out 1>&1; printf err 1>&2" prints out on stdout and err on stderr
    Then the result standard output is "out"
    And the result standard error is "err"
    And the result standard output does not contain "err"

  Scenario: A megabyte on each stream arrives whole
    Given the program "sh" with arguments:
      | -c                                                                              |
      | head -c 1048576 /dev/zero \| tr '\0' y; head -c 1048576 /dev/zero \| tr '\0' y 1>&2 |
    When I run the command
    # sh -c "head -c 1048576 /dev/zero | tr '\0' y" prints 1048576 bytes
    Then the result standard output is 1048576 bytes long
    # sh -c "head -c 1048576 /dev/zero | tr '\0' y 1>&2" writes 1048576 bytes to standard error
    And the result standard error is 1048576 bytes long

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program unchanged
    Given the program "sh" with arguments:
      | -c              |
      | printf %s "$1" |
      | _               |
      | a "b" $c        |
    When I run the command
    # sh -c 'printf %s "$1"' _ 'a "b" $c' prints a "b" $c
    Then the result standard output is 'a "b" $c'

  Scenario: The program runs in the given working directory
    Given the program "sh" with arguments:
      | -c  |
      | pwd |
    And the working directory "/tmp"
    When I run the command
    # sh -c pwd, run from /tmp, prints /tmp
    Then the result standard output is "/tmp\n"

  Scenario: An extra environment variable is visible to the program
    Given the program "sh" with arguments:
      | -c                              |
      | printf %s "$GREETING_TOKEN"      |
    And the environment variable "GREETING_TOKEN" is "xyzzy"
    When I run the command
    # env GREETING_TOKEN=xyzzy sh -c 'printf %s "$GREETING_TOKEN"' prints xyzzy
    Then the result standard output is "xyzzy"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the environment already has "GREETING_TOKEN" set to "old-value"
    And the program "sh" with arguments:
      | -c                              |
      | printf %s "$GREETING_TOKEN"      |
    And the environment variable "GREETING_TOKEN" is "new-value"
    When I run the command
    # env GREETING_TOKEN=old-value sh -c 'GREETING_TOKEN=new-value; printf %s "$GREETING_TOKEN"' prints new-value
    Then the result standard output is "new-value"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the environment already has "GREETING_TOKEN" set to "old-value"
    And the program "sh" with arguments:
      | -c                              |
      | printf %s "$GREETING_TOKEN"      |
    When I run the command
    # env GREETING_TOKEN=old-value sh -c 'printf %s "$GREETING_TOKEN"' prints old-value
    Then the result standard output is "old-value"

  Scenario: A program still running when the timeout passes is stopped and reported
    Given the program "sh" with arguments:
      | -c       |
      | sleep 5 |
    And the timeout is 1 seconds
    When I run the command
    Then running the command throws a TimeoutException whose message names "sh" and "1 seconds"

  Scenario: A program on no PATH makes the runner throw
    Given the program "no-such-program-xyz"
    When I run the command
    Then running the command throws an exception whose message names "no-such-program-xyz"

  Scenario: A caller tested against a mocked CommandRunner starts no program
    Given a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code 0, standard output "canned-output" and standard error ""
    When the caller under test runs using the mock
    Then the caller's result matches the canned CommandResult
    And no program was started
