Feature: CommandRunner runs a program with a timeout and returns its exit code and output

  CommandRunner in src/Core is the one place in the library that starts a program.
  Its RunAsync takes the program, its arguments, a working directory, extra
  environment variables and a timeout, and returns a CommandResult that holds the
  exit code, the standard output and the standard error. The factory runs on Linux,
  and sh is on the PATH wherever the tests run.

  Scenario: A program that exits with code 3 gives a CommandResult whose exit code is 3
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'exit 3'
    # sh -c 'exit 3'; echo $?  → 3, and nothing on stdout or stderr
    Then the CommandResult has exit code 3 and empty standard output and empty standard error

  Scenario: The result's standard output holds all the text the program wrote to standard output
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'echo hello stdout'
    # sh -c 'echo hello stdout'  → hello stdout on stdout, nothing on stderr (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds "hello stdout"

  Scenario: The result's standard error holds all the text the program wrote to standard error and the standard output holds none of it
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'echo error text >&2'
    # sh -c 'echo error text >&2'  → error text on stderr, nothing on stdout (exit 0)
    Then the CommandResult has exit code 0 and its standard error holds "error text"
    # sh -c 'echo error text >&2' 2>/dev/null | wc -c  → 0
    And its standard output holds none of "error text"

  Scenario: A program that writes 1 MB to standard output and 1 MB to standard error completes and both outputs arrive whole
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'head -c 1048576 /dev/zero | tr "\0" y; head -c 1048576 /dev/zero | tr "\0" e >&2'
    # sh -c 'head -c 1048576 /dev/zero | tr "\0" y; head -c 1048576 /dev/zero | tr "\0" e >&2'  → 1048576 "y" on stdout and 1048576 "e" on stderr (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds 1048576 bytes, all of them the character "y"
    # sh -c 'head -c 1048576 /dev/zero | tr "\0" y; head -c 1048576 /dev/zero | tr "\0" e >&2' 2>&1 >/dev/null | wc -c  → 1048576
    And its standard error holds 1048576 bytes, all of them the character "e"

  Scenario: An argument that holds a space, a double quote or a $ reaches the program as one argument character for character
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'printf %s "$0"' and 'a "b" $c'
    # sh -c 'printf %s "$0"' 'a "b" $c'  → a "b" $c with no trailing newline (exit 0)
    Then the CommandResult has exit code 0 and its standard output is exactly 'a "b" $c'

  Scenario: The program runs in the given working directory and pwd prints that directory
    Given the working directory is /tmp
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'pwd'
    # (cd /tmp; sh -c 'pwd')  → /tmp (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds "/tmp"

  Scenario: An extra environment variable is visible to the program with its given value
    Given the extra environment variable FACTORY_EXTRA is "extra-value"
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'echo $FACTORY_EXTRA'
    # FACTORY_EXTRA=extra-value sh -c 'echo $FACTORY_EXTRA'  → extra-value (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds "extra-value"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the extra environment variable PATH is "/replacement"
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'echo $PATH'
    # PATH=/replacement sh -c 'echo $PATH'  → /replacement (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds "/replacement"

  Scenario: An inherited variable that the extra variables leave out is visible to the program with its inherited value
    Given the extra environment variable FACTORY_EXTRA is "extra-value"
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'echo $PATH'
    # sh -c 'echo $PATH'  → the inherited value of PATH, unchanged (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds the inherited value of PATH

  Scenario: A program that finishes before the timeout returns its CommandResult
    Given the timeout is 60 seconds
    When I run sh with the arguments -c 'echo within time'
    # sh -c 'echo within time'  → within time (exit 0)
    Then the CommandResult has exit code 0 and its standard output holds "within time"

  Scenario: A program that still runs when the timeout passes is stopped and RunAsync throws a TimeoutException that names the program and the timeout
    Given the timeout is 2 seconds
    When I run sh with the arguments -c 'sleep 60'
    Then RunAsync throws a TimeoutException whose message names "sh" and "2 seconds"
    And the program is stopped

  Scenario: A program that is on no PATH makes RunAsync throw an exception that names the program
    Given the timeout is 60 seconds
    When I run no-such-program with the argument --version
    Then RunAsync throws an exception whose message names "no-such-program"
