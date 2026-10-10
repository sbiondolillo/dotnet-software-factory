Feature: The plan phase removes each feature file whose behaviour the issue removes or replaces

  Background:
    Given an empty work tree
    And the issue has the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the model is "claude-haiku-4-5-20251001" and the turn limit is 40

  Scenario: The first prompt tells the agent to remove such a feature file
    Given the agent on the first run writes the sound feature file and the plan
    When I run the plan phase
    Then the first prompt holds "Remove each feature file whose behaviour the issue removes or replaces."

  Scenario: A plan that writes the sound feature file and removes the greeting file passes
    Given the work tree holds the greeting file before the first prompt
    And the agent on the first run writes the sound feature file and the plan
    And the agent on the first run removes the greeting file
    When I run the plan phase
    Then the plan phase ran one prompt
    And the result is passed
    And the pin holds only "specs/features/checkout.feature"

  Scenario: A plan that only removes the greeting file passes while another feature file remains
    Given the work tree holds the sound feature file before the first prompt
    And the work tree holds the greeting file before the first prompt
    And the agent on the first run removes the greeting file
    And the agent on the first run writes the plan
    When I run the plan phase
    Then the plan phase ran one prompt
    And the result is passed
    And the pin holds only "specs/features/checkout.feature"

  Scenario: A plan that removes the only feature file fails naming the feature directory
    Given the work tree holds the greeting file before the first prompt
    And the agent on every run removes the greeting file
    And the agent on every run writes the plan
    When I run the plan phase
    Then the result is failed
    And a failure names "specs/features/"
