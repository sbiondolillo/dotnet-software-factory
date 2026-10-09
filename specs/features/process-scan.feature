Feature: A test checks only the programs that its own test run started

  Scenario: Programs with the same arguments from another session and another test run do not fail the check that a cancelled program is gone
    Given another session's program "sleep 16" is running, with no test-run marker
    And another test run's program "sleep 16" is running
    And the command is "sh" with arguments "-c" and "sleep 16"
    When I run the command and cancel the token 1 second after the call starts
    Then RunAsync throws an OperationCanceledException
    And the "sleep 16" program is no longer running
    And the other session's and the other test run's "sleep 16" programs are still running

  Scenario: A program of this test run that outlives the check fails it with a message that names its process id, its state and its parent
    Given the timeout is 3 seconds
    And the command is "sh" with arguments "-c" and "sleep 17"
    When I start the command and wait until the "sleep 17" program runs
    And the check that the "sleep 17" program is no longer running runs
    Then the exception message contains "The program sleep 17 still runs after 1 s: pid "
    And the exception message names the process id of the "sleep 17" program, the state "S" and the process id of its parent "sh -c sleep 17"
