Feature: The plan phase writes the scenarios and the plan, and the scenarios are pinned

  Background:
    Given an empty work tree
    And the issue has the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the model is "claude-haiku-4-5-20251001" and the turn limit is 40

  @bullet-1
  Scenario: The first prompt carries the issue, the model and the turn limit
    Given the agent on the first run writes the sound feature file and the plan
    When I run the plan phase
    Then the first prompt ran with the model "claude-haiku-4-5-20251001" and the turn limit 40
    And the first prompt holds "Add a checkout total", "The checkout shows the total of the cart.", "specs/features/" and ".factory/plan.md"

  @bullet-2
  Scenario: A sound feature file and a plan pass with one prompt and a pin
    Given the agent on the first run writes the sound feature file and the plan
    When I run the plan phase
    Then the plan phase ran one prompt
    And the result is passed
    And the pin holds "specs/features/checkout.feature" with the hash of its bytes

  @bullet-3
  Scenario: A changed feature file passes
    Given the work tree holds the sound feature file before the first prompt
    And the agent on the first run adds the second scenario to the sound feature file and writes the plan
    When I run the plan phase
    Then the result is passed

  @bullet-4
  Scenario: Notes, git and build output are not counted as changed files
    Given the agent on the first run writes the sound feature file and the plan
    And the agent on the first run writes the file ".factory/notes.md"
    And the agent on the first run writes the file ".git/index"
    And the agent on the first run writes the file "src/Core/obj/project.assets.json"
    When I run the plan phase
    Then the result is passed

  @bullet-5
  Scenario: No feature file fails naming the feature directory
    Given the agent on every run writes the plan
    When I run the plan phase
    Then the result is failed
    And a failure names "specs/features/"

  @bullet-6
  Scenario: No plan fails naming the plan
    Given the agent on every run writes the sound feature file
    When I run the plan phase
    Then the result is failed
    And a failure names ".factory/plan.md"

  @bullet-7
  Scenario: A feature file that does not parse fails naming it
    Given the agent on every run writes the sound feature file and the plan
    And the agent on every run writes the file "specs/features/broken.feature" with the text "This is not a feature file."
    When I run the plan phase
    Then the result is failed
    And a failure names "specs/features/broken.feature"

  @bullet-8
  Scenario: An empty feature file fails naming it
    Given the agent on every run writes the sound feature file and the plan
    And the agent on every run writes the file "specs/features/empty.feature" with no text
    When I run the plan phase
    Then the result is failed
    And a failure names "specs/features/empty.feature"

  @bullet-9
  Scenario: A new file outside the feature directory fails naming it
    Given the agent on every run writes the sound feature file and the plan
    And the agent on every run writes the file "src/Core/Extra.cs" with the text "class Extra {}"
    When I run the plan phase
    Then the result is failed
    And a failure names "src/Core/Extra.cs"

  @bullet-10
  Scenario: A changed file outside the feature directory fails naming it
    Given the work tree holds the file "global.json" with the text "{}" before the first prompt
    And the agent on every run writes the sound feature file and the plan
    And the agent on every run changes one byte of "global.json"
    When I run the plan phase
    Then the result is failed
    And a failure names "global.json"

  @bullet-11
  Scenario: A removed file outside the feature directory fails naming it
    Given the work tree holds the file "README.md" with the text "Readme" before the first prompt
    And the agent on every run writes the sound feature file and the plan
    And the agent on every run removes "README.md"
    When I run the plan phase
    Then the result is failed
    And a failure names "README.md"

  @bullet-12
  Scenario: The second prompt holds the issue and the failure of the first run
    Given the agent on the first run writes the sound feature file, the plan and the file "src/Core/Extra.cs"
    When I run the plan phase
    Then the second prompt holds "Add a checkout total", "The checkout shows the total of the cart." and "src/Core/Extra.cs"

  @bullet-13
  Scenario: The correction removes the extra file and passes with a pin
    Given the agent on the first run writes the sound feature file, the plan and the file "src/Core/Extra.cs"
    And the agent on the second run removes "src/Core/Extra.cs"
    When I run the plan phase
    Then the result is passed
    And the pin holds "specs/features/checkout.feature"

  @bullet-14
  Scenario: A correction that breaks another file fails with that one failure
    Given the agent on the first run writes the sound feature file, the plan and the file "src/Core/Extra.cs"
    And the agent on the second run removes "src/Core/Extra.cs"
    And the agent on the second run writes the file "src/Core/Other.cs" with the text "class Other {}"
    When I run the plan phase
    Then the plan phase ran two prompts
    And the result is failed
    And the result holds 1 failure
    And the failure names "src/Core/Other.cs"

  @bullet-15
  Scenario: An error answer fails with the result text
    Given claude answers each prompt with the error flag set and the result text "Invalid API key"
    When I run the plan phase
    Then the result is failed
    And a failure holds "Invalid API key"

  @bullet-16
  Scenario: An error answer with no result text sends no second prompt
    Given the agent on the first run writes the sound feature file and the plan
    And claude answers the first run with the error flag set and no result text
    When I run the plan phase
    Then the plan phase ran one prompt

  @bullet-17
  Scenario: Every command run through the command runner is a docker command
    Given the agent on the first run writes the sound feature file and the plan
    When I run the plan phase
    Then the result is passed
    And each command the plan phase ran is a docker command
