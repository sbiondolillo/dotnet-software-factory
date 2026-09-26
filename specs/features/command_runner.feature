Feature: A command runs with a timeout and returns its exit code and output

  Scenario: A program's exit code is returned
    Given a command "sh"
    And the command's arguments are:
      | argument |
      | -c       |
      | exit 3   |
    When I run the command
    # run: sh -c "exit 3"; echo $?
    Then the result's exit code is 3

  Scenario: Standard output and standard error are captured separately
    Given a command "sh"
    And the command's arguments are:
      | argument                                             |
      | -c                                                    |
      | printf 'stdout-text' 1>&1; printf 'stderr-text' 1>&2  |
    When I run the command
    # run: sh -c "printf 'stdout-text' 1>&1; printf 'stderr-text' 1>&2" 1>/tmp/rr2_out 2>/tmp/rr2_err; cat /tmp/rr2_out; cat /tmp/rr2_err
    Then the result's standard output is "stdout-text"
    And the result's standard error is "stderr-text"

  Scenario: A large amount of output on both streams arrives whole
    Given a command "sh"
    And the command's arguments are:
      | argument                                                                                         |
      | -c                                                                                                |
      | head -c 1048576 /dev/zero \| tr '\\0' y 1>&1; head -c 1048576 /dev/zero \| tr '\\0' e 1>&2         |
    When I run the command
    # run: sh -c "head -c 1048576 /dev/zero | tr '\0' y 1>&1; head -c 1048576 /dev/zero | tr '\0' e 1>&2" 1>/tmp/rr3_out 2>/tmp/rr3_err; wc -c </tmp/rr3_out; wc -c </tmp/rr3_err
    Then the result's standard output has length 1048576
    And the result's standard error has length 1048576

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program whole
    Given a command "sh"
    And the command's arguments are:
      | argument          |
      | -c                |
      | printf "%s" "$1"  |
      | sh                |
      | a "b" $c          |
    When I run the command
    # run: sh -c 'printf "%s" "$1"' sh 'a "b" $c'
    Then the result's standard output is 'a "b" $c'

  Scenario: The program runs in the given working directory
    Given a command "pwd"
    And the command's working directory is "/tmp"
    When I run the command
    # run: printf '%s' "$(cd /tmp && pwd)"
    Then the result's standard output trimmed of trailing whitespace is "/tmp"

  Scenario: An extra environment variable is visible to the program
    Given a command "sh"
    And the command's arguments are:
      | argument                   |
      | -c                         |
      | printf "%s" "$MY_NEW_VAR"  |
    And the command's extra environment variables are:
      | name       | value     |
      | MY_NEW_VAR | new-value |
    When I run the command
    # run: MY_NEW_VAR=new-value sh -c 'printf "%s" "$MY_NEW_VAR"'
    Then the result's standard output is "new-value"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the process environment includes "MY_VAR" set to "inherited-value"
    And a command "sh"
    And the command's arguments are:
      | argument               |
      | -c                     |
      | printf "%s" "$MY_VAR"  |
    And the command's extra environment variables are:
      | name   | value          |
      | MY_VAR | override-value |
    When I run the command
    # run: MY_VAR=override-value sh -c 'printf "%s" "$MY_VAR"'
    Then the result's standard output is "override-value"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the process environment includes "MY_OTHER_VAR" set to "inherited-value"
    And a command "sh"
    And the command's arguments are:
      | argument                    |
      | -c                          |
      | printf "%s" "$MY_OTHER_VAR" |
    When I run the command
    # run: MY_OTHER_VAR=inherited-value sh -c 'printf "%s" "$MY_OTHER_VAR"'
    Then the result's standard output is "inherited-value"

  Scenario: A program that outlasts its timeout is stopped and RunAsync throws
    Given a command "sh"
    And the command's arguments are:
      | argument |
      | -c       |
      | sleep 5  |
    And the command's timeout is 1 second
    When I run the command
    Then running the command throws a TimeoutException
    # run: printf 'sh\n1 second\n'
    And the exception message contains "sh"
    And the exception message contains "1 second"
    And the program "sh" is no longer running

  Scenario: A program that is on no PATH makes RunAsync throw
    Given a command "definitely-not-a-real-program-xyz"
    When I run the command
    Then running the command throws an exception
    # run: printf 'definitely-not-a-real-program-xyz'
    And the exception message contains "definitely-not-a-real-program-xyz"

  Scenario: A caller can be tested against a mocked CommandRunner with no program started
    Given a caller that runs a command through CommandRunner and returns its exit code
    And a mock of CommandRunner whose RunAsync returns a canned result with exit code 7
    When the caller runs
    # run: printf '7'
    Then the caller returns 7
    And no program is started
