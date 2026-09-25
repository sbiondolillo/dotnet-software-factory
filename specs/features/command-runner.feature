Feature: CommandRunner runs a program with a timeout and returns its exit code and output

  Scenario: A program that exits with code 3 gives a CommandResult whose exit code is 3
    Given a program that prints "hello" and exits with code 3
    When I run it with CommandRunner
    Then the CommandResult exit code is 3

  Scenario: The result's standard output holds all the text the program wrote to standard output
    Given a program that prints "hello stdout"
    When I run it with CommandRunner
    Then the CommandResult standard output is "hello stdout"

  Scenario: The result's standard error holds all the text the program wrote to standard error and the standard output holds none of it
    Given a program that prints "hello" to standard output and "world" to standard error
    When I run it with CommandRunner
    Then the CommandResult standard output is "hello"
    And the CommandResult standard error is "world"

  Scenario: A program that writes one megabyte to standard output and one megabyte to standard error completes and both outputs arrive whole
    # python -c 'import os; os.write(1, b"A"*1048576); os.write(2, b"B"*1048576)'
    # outputs: stdout = "A" repeated 1048576 times, stderr = "B" repeated 1048576 times
    Given a program that writes one megabyte to standard output and one megabyte to standard error
    When I run it with CommandRunner
    Then the CommandResult standard output is 1048576 bytes and every byte is "A"
    And the CommandResult standard error is 1048576 bytes and every byte is "B"

  Scenario: An argument that holds a space, a double quote or a dollar sign reaches the program as one argument, character for character
    Given the argument `a b "c" $d`
    When I run the program with that argument
    Then the program saw exactly one argument
    And the program saw the argument `a b "c" $d` character for character

  Scenario: The program runs in the given working directory and pwd prints that directory
    Given the working directory is `/tmp/command-runner-workdir-28da2742`
    When I run `pwd` with CommandRunner
    Then the CommandResult standard output is `/tmp/command-runner-workdir-28da2742`

  Scenario: An extra environment variable is visible to the program with its given value
    Given the extra environment variable FACTORY_EXTRA as "extra-value"
    When I run the program that prints its FACTORY_EXTRA environment variable
    Then the CommandResult standard output is "extra-value"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the inherited environment variable HOME as "inherited-home"
    And the extra environment variable HOME as "extra-home"
    When I run the program that prints its HOME environment variable
    Then the CommandResult standard output is "extra-home"

  Scenario: An inherited variable that the extra variables leave out is visible to the program with its inherited value
    Given the inherited environment variable HOME as "inherited-home"
    And the extra environment variable FACTORY_EXTRA as "extra-value"
    When I run the program that prints its HOME environment variable
    Then the CommandResult standard output is "inherited-home"

  Scenario: A program that still runs when the timeout passes is stopped and RunAsync throws a TimeoutException whose message names the program and the timeout
    Given a program that sleeps for ten seconds
    When I run it with CommandRunner with a timeout of one second
    Then RunAsync throws a TimeoutException
    And the exception message names the program `sh`
    And the exception message names the timeout `1 second`
    And the program is stopped

  Scenario: A program that is on no PATH makes RunAsync throw an exception whose message names the program
    Given a program that is on no PATH
    When I run it with CommandRunner
    Then RunAsync throws an exception
    And the exception message names the program
