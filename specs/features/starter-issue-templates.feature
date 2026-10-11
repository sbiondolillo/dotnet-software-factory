Feature: A repo made from the starter holds an issue template for each recipe

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  Scenario: A filled behaviour template gives its scenario to a feature file of the copy
    Given the copy's ".github/ISSUE_TEMPLATE/behaviour.md" with "<name>" replaced by "greeting" and each other placeholder replaced by "x"
    When the scenarios of that body are applied to the copy
    Then the apply gives no fault
    And the copy's "specs/features/greeting.feature" ends with one blank line and the scenario of the template's block

  Scenario: The behaviour template as it stands gets the fault of its path line
    When the scenarios of the copy's ".github/ISSUE_TEMPLATE/behaviour.md" are applied to the copy
    Then the apply gives the one fault "Each fenced block under the heading ## Scenarios starts with the line # specs/features/<name>.feature. A block starts with '# specs/features/<name>.feature'."

  Scenario: The issue template directory holds the four templates and no other file
    Then the copy's directory ".github/ISSUE_TEMPLATE" holds the files "behaviour.md", "refactor.md", "cleanup.md" and "reframe.md" and no other file
