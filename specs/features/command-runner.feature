Feature: Command execution
  The factory starts a program through CommandRunner and reads its exit code and its output.

  Scenario: A program that exits with code 3 gives a result whose exit code is 3
    Given the program "sh" with arguments "-c" and "exit 3"
    When the command runs
    # sh -c 'exit 3'; echo $? -> 3
    Then the result's exit code is 3

  Scenario: The result's standard output holds the text the program wrote to standard output
    Given the program "sh" with arguments "-c" and "echo hello world"
    When the command runs
    # sh -c 'echo hello world' -> hello world
    Then the result's standard output is "hello world"

  Scenario: The result's standard error holds the text the program wrote to standard error and the standard output holds none of it
    Given the program "sh" with arguments "-c" and "echo oops 1>&2"
    When the command runs
    # sh -c 'echo oops 1>&2' -> stdout "", stderr "oops\n"
    Then the result's standard error is "oops"
    And the result's standard output does not contain "oops"

  Scenario: A program that writes 1 MB to standard output and 1 MB to standard error completes and both outputs arrive whole
    Given the program "sh" with arguments "-c" and "yes x | head -c 1048576; yes y | head -c 1048576 1>&2"
    When the command runs
    # sh -c 'echo $((1024*1024))' -> 1048576
    Then the result's standard output is 1048576 characters long
    And the result's standard error is 1048576 characters long

  Scenario: An argument holding a space, a double quote and a dollar sign reaches the program as one argument, character for character
    Given the program "sh" with arguments "-c", "echo $#: $1", "factory-arg0" and "a b \"c\" $d"
    When the command runs
    # sh -c 'echo $#: $1' factory-arg0 'a b "c" $d' -> 1: a b "c" $d
    Then the result's standard output contains "1: a b \"c\" $d"

  Scenario: The program runs in the given working directory
    Given the working directory is "/tmp/factory-cmdrun/wd"
    Given the program "pwd" with no arguments
    When the command runs
    # mkdir -p /tmp/factory-cmdrun/wd && cd /tmp/factory-cmdrun/wd && pwd -> /tmp/factory-cmdrun/wd
    Then the result's standard output is "/tmp/factory-cmdrun/wd"

  Scenario: An extra environment variable is visible to the program with its given value
    Given the program "sh" with arguments "-c" and "echo $FACTORY_TEST_VAR"
    Given the extra environment variable "FACTORY_TEST_VAR" with value "extra-value"
    When the command runs
    # env FACTORY_TEST_VAR=extra-value sh -c 'echo $FACTORY_TEST_VAR' -> extra-value
    Then the result's standard output is "extra-value"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the environment variable "FACTORY_INHERITED_VAR" is "inherited-value"
    Given the program "sh" with arguments "-c" and "echo $FACTORY_INHERITED_VAR"
    Given the extra environment variable "FACTORY_INHERITED_VAR" with value "replaced-value"
    When the command runs
    # env FACTORY_INHERITED_VAR=replaced-value sh -c 'echo $FACTORY_INHERITED_VAR' -> replaced-value
    Then the result's standard output is "replaced-value"

  Scenario: An inherited variable the extra variables leave out is visible to the program with its inherited value
    Given the environment variable "FACTORY_INHERITED_VAR" is "inherited-value"
    Given the program "sh" with arguments "-c" and "echo $FACTORY_INHERITED_VAR"
    When the command runs
    # env FACTORY_INHERITED_VAR=inherited-value sh -c 'echo $FACTORY_INHERITED_VAR' -> inherited-value
    Then the result's standard output is "inherited-value"

  Scenario: A program that still runs when the timeout passes is stopped and RunAsync throws a TimeoutException naming the program and the timeout
    Given the program "sh" with arguments "-c" and "sleep 30"
    Given the timeout is 1 second
    When the command runs
    Then RunAsync throws a TimeoutException whose message contains "sh" and "1 second"
    And no process matching "sleep 30" runs

  Scenario: A program that is on no PATH makes RunAsync throw an exception that names the program
    Given the program "factory-no-such-program-9f3d"
    When the command runs
    Then RunAsync throws an exception whose message contains "factory-no-such-program-9f3d"
