Feature: CommandRunner

  Scenario: A program that exits with code 3 gives a result with exit code 3
    Given the command is "sh" with arguments "-c" and "exit 3"
    When I run the command
    # sh -c "exit 3"; echo $?
    # 3
    Then the exit code is 3

  Scenario: The standard output holds all the text the program wrote to it
    Given the command is "sh" with arguments "-c" and "printf 'line one\nline two\n'"
    When I run the command
    # sh -c "printf 'line one\nline two\n'"
    # line one
    # line two
    Then the standard output contains "line one"
    And the standard output contains "line two"

  Scenario: The standard error holds its own text and the standard output none of it
    Given the command is "sh" with arguments "-c" and "echo to-stdout; echo to-stderr 1>&2"
    When I run the command
    # sh -c "echo to-stdout; echo to-stderr 1>&2" 2>/dev/null
    # to-stdout
    # sh -c "echo to-stdout; echo to-stderr 1>&2" 2>&1 1>/dev/null
    # to-stderr
    Then the standard output contains "to-stdout"
    And the standard output does not contain "to-stderr"
    And the standard error contains "to-stderr"
    And the standard error does not contain "to-stdout"

  Scenario: A program that writes 1 MB to both outputs completes with both outputs whole
    Given the command is "sh" with arguments "-c" and 'i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf y;done;i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf e 1>&2;done'
    When I run the command
    # sh -c 'i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf y;done;i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf e 1>&2;done' 2>/dev/null | tr -d y | wc -c
    # 0
    Then the standard output is the letter y repeated 1048576 times
    # sh -c 'i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf y;done;i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf e 1>&2;done' 2>/dev/null | wc -c
    # 1048576
    And the standard output is 1048576 bytes
    # sh -c 'i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf y;done;i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf e 1>&2;done' 2>&1 1>/dev/null | tr -d e | wc -c
    # 0
    And the standard error is the letter e repeated 1048576 times
    # sh -c 'i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf y;done;i=0;while [ $i -lt 1048576 ];do i=$((i+1));printf e 1>&2;done' 2>&1 1>/dev/null | wc -c
    # 1048576
    And the standard error is 1048576 bytes

  Scenario: An argument with a space, a double quote and a dollar sign reaches the program unchanged
    Given the command is "sh" with arguments "-c" and 'printf %s "$1"' and "--" and 'it has a space, a "double quote" and a $ sign'
    When I run the command
    # sh -c 'printf %s "$1"' -- 'it has a space, a "double quote" and a $ sign'
    # it has a space, a "double quote" and a $ sign
    Then the standard output is 'it has a space, a "double quote" and a $ sign'

  Scenario: The program runs in the given working directory
    Given the working directory is "/tmp/factory-command-runner"
    And the command is "sh" with arguments "-c" and "pwd"
    When I run the command
    # (mkdir -p /tmp/factory-command-runner && cd /tmp/factory-command-runner && sh -c "pwd")
    # /tmp/factory-command-runner
    Then the standard output contains "/tmp/factory-command-runner"

  Scenario: An extra environment variable is visible to the program with its given value
    Given the extra environment variable "FACTORY_EXTRA" is "pineapple"
    And the command is "sh" with arguments "-c" and "printenv FACTORY_EXTRA"
    When I run the command
    # FACTORY_EXTRA=pineapple sh -c "printenv FACTORY_EXTRA"
    # pineapple
    Then the standard output contains "pineapple"

  Scenario: An extra environment variable replaces an inherited variable of the same name
    Given the inherited environment variable "FACTORY_EXTRA" is "mango"
    And the extra environment variable "FACTORY_EXTRA" is "pineapple"
    And the command is "sh" with arguments "-c" and "printenv FACTORY_EXTRA"
    When I run the command
    # FACTORY_EXTRA=mango FACTORY_EXTRA=pineapple sh -c "printenv FACTORY_EXTRA"
    # pineapple
    Then the standard output contains "pineapple"
    And the standard output does not contain "mango"

  Scenario: An inherited variable the extra variables leave out keeps its inherited value
    Given the inherited environment variable "FACTORY_INHERITED" is "quince"
    And the extra environment variable "FACTORY_OTHER" is "pineapple"
    And the command is "sh" with arguments "-c" and "printenv FACTORY_INHERITED"
    When I run the command
    # FACTORY_INHERITED=quince sh -c "printenv FACTORY_INHERITED"
    # quince
    Then the standard output contains "quince"

  Scenario: A program still running when the timeout passes is stopped and RunAsync throws a TimeoutException
    Given the timeout is 2 seconds
    And the command is "sh" with arguments "-c" and "sleep 10"
    When I run the command
    Then RunAsync throws a TimeoutException
    And the exception message contains "sh"
    And the exception message contains "2 seconds"
    And the "sleep 10" program is no longer running

  Scenario: A program on no PATH makes RunAsync throw an exception that names the program
    Given the command is "factory-missing-program-xyz"
    When I run the command
    Then RunAsync throws an exception
    And the exception message contains "factory-missing-program-xyz"

  Scenario: A caller tested against a Moq mock of the runner gets the canned result and starts no program
    Given a caller receives a mock of CommandRunner
    And the mock returns a CommandResult with exit code 7 and standard output "canned output"
    When the caller asks the mock to run "ls"
    Then the caller sees the exit code 7
    And the caller sees the standard output "canned output"
    And no program was started
