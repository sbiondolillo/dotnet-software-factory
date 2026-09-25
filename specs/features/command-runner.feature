Feature: A command runs with a timeout and returns its exit code and output

  Scenario: A program's exit code is returned
    Given I run the program "sh" with arguments "-c" "exit 3"
    When the command completes
    # sh -c 'exit 3'; echo $? prints 3
    Then the exit code is 3

  Scenario: The program's standard output is captured in full
    Given I run the program "sh" with arguments "-c" "printf 'hello stdout'"
    When the command completes
    # sh -c "printf 'hello stdout'" prints hello stdout
    Then the standard output is "hello stdout"

  Scenario: The program's standard error is captured and kept out of standard output
    Given I run the program "sh" with arguments "-c" "printf 'oops error' 1>&2"
    When the command completes
    # sh -c "printf 'oops error' 1>&2" prints oops error on standard error and nothing on standard output
    Then the standard error is "oops error"
    And the standard output is ""

  Scenario: A megabyte of standard output and standard error both arrive whole
    Given I run a program that writes 1 MB to standard output and 1 MB to standard error
    When the command completes
    Then the standard output is 1048576 bytes long
    And the standard error is 1048576 bytes long

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program unchanged
    Given I run the program "sh" with arguments "-c" "printf %s \"$1\"" "_" "a \"b\" $HOME c"
    When the command completes
    # sh -c 'printf %s "$1"' _ 'a "b" $HOME c' prints a "b" $HOME c
    Then the standard output is "a \"b\" $HOME c"

  Scenario: The program runs in the given working directory
    Given I run the program "sh" with arguments "-c" "pwd" in the working directory "/tmp"
    When the command completes
    # cd /tmp && sh -c "pwd" prints /tmp
    Then the standard output is "/tmp"

  Scenario: An extra environment variable is visible to the program
    Given I run the program "sh" with arguments "-c" "printf %s \"$FOO\"" with the extra environment variable "FOO" set to "bar"
    When the command completes
    # env FOO=bar sh -c 'printf %s "$FOO"' prints bar
    Then the standard output is "bar"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the inherited environment variable "FOO" is set to "inherited"
    And I run the program "sh" with arguments "-c" "printf %s \"$FOO\"" with the extra environment variable "FOO" set to "override"
    When the command completes
    # env FOO=inherited sh -c 'FOO=override sh -c "printf %s \"$FOO\""' prints override
    Then the standard output is "override"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the inherited environment variable "FOO" is set to "inherited"
    And I run the program "sh" with arguments "-c" "printf %s \"$FOO\"" with no extra environment variables
    When the command completes
    # FOO=inherited sh -c 'printf %s "$FOO"' prints inherited
    Then the standard output is "inherited"

  Scenario: A program that outlasts its timeout is stopped and RunAsync reports it
    Given I run the program "sh" with arguments "-c" "sleep 5" with a timeout of 1 seconds
    When the command runs past its timeout
    Then RunAsync throws a TimeoutException
    And the exception message names the program "sh"
    And the exception message names the timeout "1"

  Scenario: A program on no PATH makes RunAsync throw an exception naming it
    Given I run the program "no-such-program-xyz" with arguments (none)
    When the command fails to start
    Then RunAsync throws an exception
    And the exception message names the program "no-such-program-xyz"

  Scenario: A caller tested against a mocked CommandRunner passes without starting a program
    Given a caller that depends on CommandRunner
    And a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code 0, standard output "canned output" and standard error ""
    When the caller runs using the mocked CommandRunner
    Then the caller receives the canned CommandResult
    And no program starts
