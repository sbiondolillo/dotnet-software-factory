Feature: The lint gate formats the build and commits the result

  @bullet-1
  Scenario: The lint gate runs dotnet format through the Sandbox
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing
    And git reports no change in the work tree
    When a caller runs the lint gate on a work tree
    Then a docker exec command runs dotnet format

  @bullet-2
  Scenario: A changed work tree gets one commit with the caller's message and author
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing
    And git reports one change in the work tree
    When a caller runs the lint gate with the message "Add a checkout total", the author name "Ada Lovelace" and the author email "ada@example.com"
    Then the git commit carries every change of the work tree
    # run: printf 'Add a checkout total\nAda Lovelace\nada@example.com'
    And the git commit carries the message "Add a checkout total", the author name "Ada Lovelace" and the author email "ada@example.com"
    And the result says a commit was made

  @bullet-3
  Scenario: An unchanged work tree gets no commit
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing
    And git reports no change in the work tree
    When a caller runs the lint gate on a work tree
    Then no git commit runs
    And the result says no commit was made

  @bullet-4
  Scenario: The format runs first, then the change check, then the commit
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing
    And git reports one change in the work tree
    When a caller runs the lint gate on a work tree
    Then the commands run in this order
      | command       |
      | dotnet format |
      | git status    |
      | git add       |
      | git commit    |

  @bullet-5
  Scenario: A failed format throws LintGateException and runs no git command
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 1 and writes a reason to standard error
    When a caller runs the lint gate on a work tree
    Then the lint gate throws LintGateException
    # run: printf 1
    And the exception message holds the exit code 1 and the reason
    And no git command runs

  @bullet-6
  Scenario: A warning on standard error with exit code 0 and no change gives no commit
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes a line to standard error
    And git reports no change in the work tree
    When a caller runs the lint gate on a work tree
    Then the lint gate does not throw
    And no git commit runs
    And the result says no commit was made

  @bullet-7
  Scenario: Git answers that the work tree holds a change for one new file
    Given a Git built over a mock CommandRunner
    And git reports one new file "Other.cs" in the work tree
    When a caller asks Git whether the work tree holds a change
    Then Git answers that the work tree holds a change

  @bullet-8
  Scenario: Git answers that the work tree holds no change
    Given a Git built over a mock CommandRunner
    And git reports no change in the work tree
    When a caller asks Git whether the work tree holds a change
    Then Git answers that the work tree holds no change
