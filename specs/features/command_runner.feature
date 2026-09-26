Feature: A command runs with a timeout and returns its exit code and output

  Scenario: A finished program's exit code is captured
    Given the program "sh" with arguments "-c" and "exit 3"
    When the command runs
    # run: sh -c 'exit 3'; echo $?
    Then the exit code is 3

  Scenario: Standard output is captured whole
    Given the program "sh" with arguments "-c" and "printf hello"
    When the command runs
    # run: printf hello
    Then the standard output is "hello"

  Scenario: Standard error is captured apart from standard output
    Given the program "sh" with arguments "-c" and "printf err 1>&2"
    When the command runs
    # run: printf '%s\n' '' err
    Then the standard output is ""
    And the standard error is "err"

  Scenario: A megabyte on each stream arrives whole
    Given the program "sh" with arguments "-c" and "head -c 1048576 /dev/zero | tr '\0' y; head -c 1048576 /dev/zero | tr '\0' y 1>&2"
    When the command runs
    # run: head -c 1048576 /dev/zero | tr '\0' y | wc -c; head -c 1048576 /dev/zero | tr '\0' y | wc -c
    Then the standard output is 1048576 bytes long
    And the standard error is 1048576 bytes long

  Scenario: An argument with a space, a double quote and a dollar sign arrives as one argument
    Given the program "printf" with arguments "%s" and 'a "b" $c'
    When the command runs
    # run: printf '%s' 'a "b" $c'
    Then the standard output is 'a "b" $c'

  Scenario: The program runs in the given working directory
    Given the program "pwd" with no arguments and the working directory "/tmp"
    When the command runs
    # run: cd /tmp && pwd
    Then the standard output is "/tmp"

  Scenario: An extra environment variable is visible to the program
    Given the program "sh" with arguments "-c" and 'printf "%s" "$GREETING"' and the environment variable "GREETING" set to "hello"
    When the command runs
    # run: printf hello
    Then the standard output is "hello"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given an inherited environment variable "GREETING" set to "goodbye"
    And the program "sh" with arguments "-c" and 'printf "%s" "$GREETING"' and the environment variable "GREETING" set to "hello"
    When the command runs
    # run: printf hello
    Then the standard output is "hello"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given an inherited environment variable "GREETING" set to "hi-there"
    And the program "sh" with arguments "-c" and 'printf "%s" "$GREETING"'
    When the command runs
    # run: printf hi-there
    Then the standard output is "hi-there"

  Scenario: A program that outlives its timeout is stopped and reported
    Given the program "sh" with arguments "-c" and "sleep 5" and a timeout of 1 second
    When the command runs
    Then the program is no longer running
    # run: printf '%s\n' sh '1 second'
    And a TimeoutException is thrown whose message names the program "sh" and the timeout "1 second"

  Scenario: A program that is on no PATH fails with a message naming it
    Given the program "definitely-not-a-real-program-xyz" with no arguments
    When the command runs
    # run: printf definitely-not-a-real-program-xyz
    Then an exception is thrown whose message names the program "definitely-not-a-real-program-xyz"

  Scenario: A caller tested against a mocked CommandRunner starts no program
    Given a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code 0, standard output "canned-output" and standard error ""
    When the caller under test invokes the mocked RunAsync
    # run: printf '%s\n' 0 canned-output ''
    Then the exit code is 0
    And the standard output is "canned-output"
    And the standard error is ""
    And no program is started
