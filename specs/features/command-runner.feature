Feature: CommandRunner runs a program and returns its exit code and output

  CommandRunner in src/Core is the one place in the library that starts a
  program. Its RunAsync method takes the program, its arguments, a working
  directory, extra environment variables and a timeout, and returns a
  CommandResult holding the exit code, the standard output and the standard
  error.

  Scenario: A program that exits with code 3 gives a result with exit code 3
    Given the program is "sh"
    Given its arguments are "-c" and "exit 3"
    When I run the program
    # sh -c 'exit 3'; echo $? => 3
    Then the exit code is 3

  Scenario: The result's standard output holds all the text the program wrote to standard output
    Given the program is "sh"
    Given its arguments are "-c" and "echo all of this text goes to standard output"
    When I run the program
    # sh -c 'echo all of this text goes to standard output' => all of this text goes to standard output
    Then the standard output contains "all of this text goes to standard output"

  Scenario: The result's standard error holds all the text the program wrote to standard error and the standard output holds none of it
    Given the program is "sh"
    Given its arguments are "-c" and "echo the stdout line; echo the stderr line >&2"
    When I run the program
    # sh -c 'echo the stdout line; echo the stderr line >&2' 2>&1 1>/dev/null => the stderr line
    Then the standard error contains "the stderr line"
    # sh -c 'echo the stdout line; echo the stderr line >&2' 2>/dev/null => the stdout line
    And the standard output contains "the stdout line"
    # sh -c 'echo the stdout line; echo the stderr line >&2' 2>/dev/null => the stdout line
    And the standard output does not contain "the stderr line"

  Scenario: A program that writes 1 MB to standard output and 1 MB to standard error completes and both outputs arrive whole
    Given the program is "python3"
    Given its arguments are "-c" and "import sys; sys.stdout.write('A' * 1048576); sys.stderr.write('E' * 1048576)"
    When I run the program
    Then RunAsync returns without throwing
    # python3 -c "import sys; sys.stdout.write('A' * 1048576); sys.stderr.write('E' * 1048576)" >/dev/null 2>&1; echo $? => 0
    Then the exit code is 0
    # python3 -c "import sys; sys.stdout.write('A' * 1048576); sys.stderr.write('E' * 1048576)" 2>/dev/null | wc -c => 1048576
    Then the standard output is 1048576 characters long
    # python3 -c "import sys; sys.stdout.write('A' * 1048576); sys.stderr.write('E' * 1048576)" 2>&1 >/dev/null | wc -c => 1048576
    And the standard error is 1048576 characters long

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program as one argument character for character
    Given the program is "sh"
    Given its arguments are "-c" and 'printf %s "$1"' and "_" and 'a "quoted" $VAR space'
    When I run the program
    # sh -c 'printf %s "$1"' _ 'a "quoted" $VAR space' => a "quoted" $VAR space
    Then the standard output is 'a "quoted" $VAR space'

  Scenario: The program runs in the given working directory
    Given the working directory is "/tmp/factory-cwd-check"
    Given the program is "sh"
    Given its arguments are "-c" and "pwd"
    When I run the program
    # (cd /tmp/factory-cwd-check && sh -c 'pwd') => /tmp/factory-cwd-check
    Then the standard output contains "/tmp/factory-cwd-check"

  Scenario: An extra environment variable is visible to the program with its given value
    Given the extra environment variables are
      | name                  | value            |
      | FACTORY_CHECK_VALUE   | hello world 42   |
    Given the program is "sh"
    Given its arguments are "-c" and 'printf %s "$FACTORY_CHECK_VALUE"'
    When I run the program
    # FACTORY_CHECK_VALUE='hello world 42' sh -c 'printf %s "$FACTORY_CHECK_VALUE"' => hello world 42
    Then the standard output is "hello world 42"

  Scenario: An extra environment variable replaces an inherited environment variable of the same name
    Given the inherited environment variable "FACTORY_OVERRIDE_CHECK" is "the inherited value"
    Given the extra environment variables are
      | name                    | value           |
      | FACTORY_OVERRIDE_CHECK  | the extra value |
    Given the program is "sh"
    Given its arguments are "-c" and 'printf %s "$FACTORY_OVERRIDE_CHECK"'
    When I run the program
    # FACTORY_OVERRIDE_CHECK='the extra value' sh -c 'printf %s "$FACTORY_OVERRIDE_CHECK"' => the extra value
    Then the standard output is "the extra value"

  Scenario: An inherited variable the extra variables leave out is visible to the program with its inherited value
    Given the inherited environment variable "FACTORY_INHERITED_CHECK" is "the inherited value"
    Given the extra environment variables are
      | name               | value            |
      | FACTORY_UNRELATED  | unrelated value  |
    Given the program is "sh"
    Given its arguments are "-c" and 'printf %s "$FACTORY_INHERITED_CHECK"'
    When I run the program
    # FACTORY_INHERITED_CHECK='the inherited value' sh -c 'printf %s "$FACTORY_INHERITED_CHECK"' => the inherited value
    Then the standard output is "the inherited value"

  Scenario: A program that still runs when the timeout passes is stopped and RunAsync throws a TimeoutException
    Given the program is "sleep"
    Given its arguments are "5"
    When I run the program with a timeout of 2 seconds
    Then RunAsync throws a TimeoutException
    And the exception message contains "sleep"
    And the exception message contains "2 seconds"
    And the program is no longer running

  Scenario: A program that is on no PATH makes RunAsync throw an exception naming the program
    Given the program is "factory-not-a-real-program"
    When I run the program
    Then RunAsync throws an exception whose message contains "factory-not-a-real-program"
