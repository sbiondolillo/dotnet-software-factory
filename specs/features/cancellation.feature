Feature: A caller cancels any command the library runs

  @bullet-1
  Scenario: Cancelling the token 1 second in stops a running program and RunAsync throws
    Given the command is "sh" with arguments "-c" and "sleep 12"
    When I run the command and cancel the token 1 second after the call starts
    Then RunAsync throws an OperationCanceledException
    And the "sleep 12" program is no longer running

  @bullet-2
  Scenario: A token that is already cancelled starts no program
    Given the command is "sh" with arguments "-c" and "touch /tmp/factory-cancel-marker"
    When I run the command with a token that is already cancelled
    Then RunAsync throws an OperationCanceledException
    And the file "/tmp/factory-cancel-marker" does not exist

  @bullet-3
  Scenario: A cancelled token beats a longer timeout and RunAsync throws OperationCanceledException
    Given the timeout is 10 seconds
    And the command is "sh" with arguments "-c" and "sleep 30"
    When I run the command and cancel the token 1 second after the call starts
    Then RunAsync throws an OperationCanceledException

  @bullet-4
  Scenario: One cancellation token passed to a clone, checkout, commit, push, head read and changed-files read reaches every command
    Given a caller has a Git built over a mock of CommandRunner that records the cancellation token each call receives
    And the caller has one cancellation token named "token-A"
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-4" into "/tmp/factory-clone-cancel-4" using the cancellation token "token-A"
    And Git checks out the new branch "feature-cancel-4" in "/tmp/factory-clone-cancel-4" using the cancellation token "token-A"
    And Git commits all changes in "/tmp/factory-clone-cancel-4" with the message "Update README" and the author name "Ada Lovelace" and the author email "ada@example.com" using the cancellation token "token-A"
    And Git pushes the branch "feature-cancel-4" from "/tmp/factory-clone-cancel-4" with the token "token-4" using the cancellation token "token-A"
    And Git reads the head SHA in "/tmp/factory-clone-cancel-4" using the cancellation token "token-A"
    And Git lists the files changed since "main" in "/tmp/factory-clone-cancel-4" using the cancellation token "token-A"
    Then every call the mock CommandRunner recorded received the cancellation token named "token-A"

  @bullet-5
  Scenario: A cancelled add stops the commit before Git runs the commit command
    Given a caller has a Git built over a mock of CommandRunner that records every CommandSpec it runs
    And the mock CommandRunner throws OperationCanceledException when Git runs "add"
    When Git commits all changes in "/tmp/factory-clone-cancel-5" with the message "Update README" and the author name "Ada Lovelace" and the author email "ada@example.com"
    Then Git throws an OperationCanceledException
    And the mock CommandRunner recorded no "commit" command
