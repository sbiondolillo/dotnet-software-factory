Feature: A command runs with a timeout and returns its exit code and output
  CommandRunner.RunAsync starts a program with its arguments, working directory,
  extra environment variables and a timeout, and returns a CommandResult holding
  the exit code, the standard output and the standard error.

  Scenario: A program that exits with code 3 gives a CommandResult whose exit code is 3
    # sh -c 'exit 3'; echo $?
    # -> 3
    When I run "sh" with the arguments "-c" and "exit 3"
    Then the CommandResult exit code is 3

  Scenario: The standard output holds all the text the program wrote to standard output
    # sh -c 'echo hello world'
    # -> hello world
    When I run "sh" with the arguments "-c" and "echo hello world"
    Then the standard output is "hello world"

  Scenario: The standard error holds all the text the program wrote to standard error and the standard output holds none of it
    # sh -c 'echo oops >&2' 2>&1
    # -> oops
    When I run "sh" with the arguments "-c" and "echo oops >&2"
    Then the standard error is "oops"
    And the standard output is empty

  Scenario: A program that writes 1 MB to standard output and 1 MB to standard error completes and both outputs arrive whole
    # python3 -c "import sys; sys.stdout.write('a' * 1048576); sys.stderr.write('b' * 1048576)" 2>/dev/null | wc -c
    # -> 1048576
    # python3 -c "import sys; sys.stdout.write('a' * 1048576); sys.stderr.write('b' * 1048576)" 2>&1 1>/dev/null | wc -c
    # -> 1048576
    When I run "python3" with the arguments "-c" and "import sys; sys.stdout.write('a' * 1048576); sys.stderr.write('b' * 1048576)"
    Then the CommandResult exit code is 0
    And the standard output holds 1048576 characters
    And the standard error holds 1048576 characters

  Scenario: An argument that holds a space, a double quote and a $ reaches the program as one argument character for character
    # sh -c 'echo $# $1' _ 'a b "c" $d'
    # -> 1 a b "c" $d
    When I run "sh" with the arguments "-c" and "echo $# $1" and "_" and 'a b "c" $d'
    Then the standard output is '1 a b "c" $d'

  Scenario: The program runs in the given working directory
    # mkdir -p /tmp/factory-cwd && cd /tmp/factory-cwd && pwd
    # -> /tmp/factory-cwd
    When I run "pwd" in the working directory "/tmp/factory-cwd"
    Then the standard output is "/tmp/factory-cwd"

  Scenario: An extra environment variable is visible to the program with its given value
    # EXTRA_ALPHA=alpha sh -c 'echo $EXTRA_ALPHA'
    # -> alpha
    When I run "sh" with the arguments "-c" and "echo $EXTRA_ALPHA" and the extra environment variable "EXTRA_ALPHA" is "alpha"
    Then the standard output is "alpha"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    # SHADOWED=new sh -c 'echo $SHADOWED'
    # -> new
    Given the inherited environment variable "SHADOWED" is "old"
    When I run "sh" with the arguments "-c" and "echo $SHADOWED" and the extra environment variable "SHADOWED" is "new"
    Then the standard output is "new"

  Scenario: An inherited variable the extra variables leave out is visible to the program with its inherited value
    # KEPT=kept sh -c 'echo $KEPT'
    # -> kept
    Given the inherited environment variable "KEPT" is "kept"
    When I run "sh" with the arguments "-c" and "echo $KEPT" and the extra environment variable "OTHER" is "unrelated"
    Then the standard output is "kept"

  Scenario: A program that still runs when the timeout passes is stopped and RunAsync throws a TimeoutException that names the program and the timeout
    When I run "sh" with the arguments "-c" and "sleep 30" with a timeout of 1000 milliseconds
    Then RunAsync throws a TimeoutException whose message names "sh" and "1000 milliseconds"

  Scenario: A program that is on no PATH makes RunAsync throw an exception whose message names the program
    When I run "no-such-command-cd66d78d"
    Then RunAsync throws an exception whose message names "no-such-command-cd66d78d"
