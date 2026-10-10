Feature: The build phase implements the plan and binds every pinned scenario

  Background:
    Given the issue has the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the build model is "claude-haiku-4-5-20251001" and the turn limit is 60

  Scenario: The first build prompt carries the issue, the model and the turn limit
    Given the build agent answers with the error flag clear
    When I run the build phase with no Redo texts
    Then the build phase ran one prompt
    And the build prompt ran with the model "claude-haiku-4-5-20251001" and the turn limit 60
    And the build prompt holds "Add a checkout total" and "The checkout shows the total of the cart."

  Scenario: The build prompt names the plan, the features, the steps and the unit tests
    Given the build agent answers with the error flag clear
    When I run the build phase with no Redo texts
    Then the build prompt holds ".factory/plan.md", "specs/features/", "tests/Specs/Steps/" and "tests/Specs/Unit/"

  Scenario: The build prompt names the build and test commands
    Given the build agent answers with the error flag clear
    When I run the build phase with no Redo texts
    Then the build prompt holds "dotnet build" and "dotnet test"

  Scenario: The build prompt names the build settings files
    Given the build agent answers with the error flag clear
    When I run the build phase with no Redo texts
    Then the build prompt holds ".editorconfig" and "reqnroll.json"

  Scenario: An answer with the error flag clear passes
    Given the build agent answers with the error flag clear
    When I run the build phase with no Redo texts
    Then the build result is passed

  Scenario: An answer with the error flag set and a result text fails with the text
    Given the build agent answers with the error flag set and the result text "Invalid API key"
    When I run the build phase with no Redo texts
    Then the build result is failed
    And the build failure holds "Invalid API key"

  Scenario: An answer with the error flag set and no result text fails
    Given the build agent answers with the error flag set and no result text
    When I run the build phase with no Redo texts
    Then the build result is failed

  Scenario: A Redo prompt holds both Redo texts and the issue
    Given the build agent answers with the error flag clear
    When I run the build phase with the Redo texts "The test AddsTwoPrices failed: the values differ." and "The total ignores the second item of the cart."
    Then the build phase ran one prompt
    And the build prompt holds "The test AddsTwoPrices failed: the values differ.", "The total ignores the second item of the cart.", "Add a checkout total", "The checkout shows the total of the cart.", ".factory/plan.md", "specs/features/" and "tests/Specs/Steps/"

  Scenario: A Redo text over the limit is cut and the shorter text stays whole
    Given the build agent answers with the error flag clear
    When I run the build phase with the Redo texts of the letter "a" 200000 times and "The total ignores the second item of the cart."
    Then the build phase ran one prompt
    And the build prompt is 131071 bytes or fewer
    And the build prompt holds "Add a checkout total" and "The checkout shows the total of the cart."
    And the build prompt holds the letter "a" 100000 times in sequence
    And the build prompt holds "[cut]" and "The total ignores the second item of the cart."

  Scenario: A multibyte Redo text over the limit is cut on whole characters
    Given the build agent answers with the error flag clear
    When I run the build phase with one Redo text of the letter "é" 100000 times
    Then the build phase ran one prompt
    And the build prompt is 131071 bytes or fewer
    And the build prompt holds the letter "é" 50000 times in sequence and "[cut]"
