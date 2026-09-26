Feature: A caller cancels any command the library runs

  @bullet-1
  Scenario: Cancelling the token stops a program running in the background and RunAsync throws
    Given the command is "sh" with arguments "-c" and "sleep 10"
    When I run the command and cancel the token 1 second after the call starts
    Then RunAsync throws an OperationCanceledException
    # run: printf 'sleep 10'
    And the "sleep 10" program is no longer running

  @bullet-2
  Scenario: A token already cancelled before the call starts starts no program
    Given the command is "sh" with arguments "-c" and "touch /tmp/factory-cancel-marker"
    When I run the command with a token that is already cancelled
    Then RunAsync throws an OperationCanceledException
    # run: printf '/tmp/factory-cancel-marker'
    And the file "/tmp/factory-cancel-marker" does not exist

  @bullet-3
  Scenario: A cancelled token wins a race against a longer timeout
    Given the timeout is 10 seconds
    And the command is "sh" with arguments "-c" and "sleep 30"
    When I run the command and cancel the token 1 second after the call starts
    Then RunAsync throws an OperationCanceledException

  @bullet-4
  Scenario: Every git command in a full workflow receives the caller's cancellation token
    Given a caller has a Git built over a mock of CommandRunner that records every CommandSpec it runs
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-1" into "/tmp/factory-clone-cancel-4" and the cancellation token "workflow-token"
    And Git checks out the new branch "feature-cancel-4" in "/tmp/factory-clone-cancel-4" with the cancellation token "workflow-token"
    And Git commits all changes in "/tmp/factory-clone-cancel-4" with the message "Update README" and the author name "Ada Lovelace" and the author email "ada@example.com" and the cancellation token "workflow-token"
    And Git pushes the branch "feature-cancel-4" from "/tmp/factory-clone-cancel-4" with the token "token-1" and the cancellation token "workflow-token"
    And Git reads the head SHA in "/tmp/factory-clone-cancel-4" with the cancellation token "workflow-token"
    And Git lists the files changed since "main" in "/tmp/factory-clone-cancel-4" with the cancellation token "workflow-token"
    # run: printf 'workflow-token'
    Then every call the mock CommandRunner recorded received the cancellation token "workflow-token"

  @bullet-5
  Scenario: A cancelled add stops the commit before any commit command runs
    Given a caller has a Git built over a mock of CommandRunner that records every CommandSpec it runs
    And the mock CommandRunner throws OperationCanceledException for the "add" command
    When Git commits all changes in "/tmp/factory-clone-cancel-5" with the message "Update README" and the author name "Ada Lovelace" and the author email "ada@example.com"
    Then the commit throws an OperationCanceledException
    # run: printf 'commit'
    And the mock CommandRunner recorded no "commit" CommandSpec
