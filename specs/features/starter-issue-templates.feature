Feature: A repo made from the starter holds an issue template for each recipe

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  @bullet-1
  Scenario: The behaviour template holds the skeleton of a behaviour issue
    Then the copy's file ".github/ISSUE_TEMPLATE/behaviour.md" holds this text, with a line that holds "# specs/features/<name>.feature"
      """
      ---
      name: Behaviour
      about: A feature, a bug fix, or a new test for behaviour the program already has. The rules are docs/write-an-issue.md.
      title: ''
      labels: ''
      ---

      As a <role>, I want to <ability>, so that <benefit>.

      **Today.** <What `main` does now that stops that person, in 3 sentences or fewer.>

      **What changes.** <Each file and each program that a scenario names, with the kind of each thing. Then each invariant.>

      ## Scenarios

      <One block for each feature file. A file on main: the scenarios that the change adds or edits. A new file: its Feature: line first, then its scenarios. The rules are docs/write-an-issue.md.>

      ```gherkin
      # specs/features/<name>.feature
        Scenario: <what the person observes>
          Given <the state>
          When <the event>
          Then <the result>
      ```

      ## Context

      <A fact of the world that a scenario needs, with its source.>
      """

  Scenario: A filled behaviour template gives its scenario to a feature file of the copy
    Given the copy's ".github/ISSUE_TEMPLATE/behaviour.md" with "<name>" replaced by "greeting" and each other placeholder replaced by "x"
    When the scenarios of that body are applied to the copy
    Then the apply gives no fault
    And the copy's "specs/features/greeting.feature" ends with one blank line and the scenario of the template's block

  Scenario: The behaviour template as it stands gets the fault of its path line
    When the scenarios of the copy's ".github/ISSUE_TEMPLATE/behaviour.md" are applied to the copy
    Then the apply gives the one fault "Each fenced block under the heading ## Scenarios starts with the line # specs/features/<name>.feature. A block starts with '# specs/features/<name>.feature'."

  @bullet-2
  Scenario: The refactor template holds the skeleton of a refactor issue
    Then the copy's file ".github/ISSUE_TEMPLATE/refactor.md" holds this text
      """
      ---
      name: Refactor
      about: A change to the structure of the code that adds no behaviour. Every test stays as it is. The rules are docs/write-an-issue.md.
      title: ''
      labels: refactor
      ---

      Refactor. <What moves and where, the invariant, and each constraint that the tests impose.>

      ## Check

      ```sh
      <a command that fails on main and exits with the code 0 after the change>
      ```
      """

  @bullet-3
  Scenario: The cleanup template holds the skeleton of a cleanup issue
    Then the copy's file ".github/ISSUE_TEMPLATE/cleanup.md" holds this text
      """
      ---
      name: Cleanup
      about: A fix to a test, a config file or a document. The program stays as it is. The rules are docs/write-an-issue.md.
      title: ''
      labels: cleanup
      ---

      Cleanup. <The defect, what the test must prove, and the invariant.>

      ## Scope

      - `<path>`

      ## Check

      ```sh
      <a command that fails on main and exits with the code 0 after the change>
      ```
      """

  Scenario: The reframe template holds the skeleton of a reframe issue
    Then the copy's file ".github/ISSUE_TEMPLATE/reframe.md" holds this text
      """
      ---
      name: Reframe
      about: A change to the code, the tests or the config that keeps the behaviour, such as a format sweep or a framework upgrade. Every passing test keeps passing, and the public API of src/ stays as it is. The rules are docs/write-an-issue.md.
      title: ''
      labels: reframe
      ---

      Reframe. <The rule or the version that changes, the paths it touches, and the invariant.>

      ## Scope

      - `<path>`

      ## Check

      ```sh
      <a command that fails on main and exits with the code 0 after the change>
      ```
      """

  Scenario: The issue template directory holds the four templates and no other file
    Then the copy's directory ".github/ISSUE_TEMPLATE" holds the files "behaviour.md", "refactor.md", "cleanup.md" and "reframe.md" and no other file
