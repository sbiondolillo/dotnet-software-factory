Feature: The lint gate formats the build and commits the result

  @bullet-1
  Scenario: The lint gate runs dotnet format through the Sandbox
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing to standard error
    And git reports no change in the work tree
    When a caller runs the lint gate on a work tree
    Then the Sandbox ran dotnet format

  @bullet-2
  Scenario: A changed work tree gets one commit with the caller's message and author
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing to standard error
    And git reports one change, the new file "Other.cs"
    When a caller runs the lint gate with the message "Add a checkout total", the author name "Ada Lovelace" and the author email "ada@example.com"
    Then LintGate made one commit of every change
    And the commit carries the message "Add a checkout total", the author name "Ada Lovelace" and the author email "ada@example.com"
    And the result says a commit was made

  @bullet-3
  Scenario: An unchanged work tree gets no commit
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing to standard error
    And git reports no change in the work tree
    When a caller runs the lint gate on a work tree
    Then LintGate made no commit
    And the result says no commit was made

  @bullet-4
  Scenario: The format runs first, then the change query, then the commit
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes nothing to standard error
    And git reports one change, the new file "Other.cs"
    When a caller runs the lint gate on a work tree
    Then the commands ran in this order: dotnet format, the git change query, the git commit
    And the git commit was the last command

  @bullet-5
  Scenario: A failed format throws LintGateException and runs no git command
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 1 and writes to standard error "Unhandled exception: System.IO.FileNotFoundException: Could not find a MSBuild project file or solution file"
    When a caller runs the lint gate on a work tree
    Then the lint gate throws LintGateException
    And the exception message holds "1" and "Unhandled exception: System.IO.FileNotFoundException: Could not find a MSBuild project file or solution file"
    And no git command ran

  @bullet-6
  Scenario: A standard error line with exit code 0 and no change gives no commit
    Given a LintGate built over a Sandbox and a Git over a mock CommandRunner
    And dotnet format exits with the code 0 and writes a line to standard error
    And git reports no change in the work tree
    When a caller runs the lint gate on a work tree
    Then the result says no commit was made
    And LintGate made no commit

  @bullet-7
  Scenario: Git answers that a work tree with one new file holds a change
    Given a Git built over a mock CommandRunner
    And git reports one change, the new file "Other.cs"
    When a caller asks Git whether the work tree holds a change
    Then Git answers that the work tree holds a change

  @bullet-8
  Scenario: Git answers that a work tree with no change holds no change
    Given a Git built over a mock CommandRunner
    And git reports no change in the work tree
    When a caller asks Git whether the work tree holds a change
    Then Git answers that the work tree holds no change
