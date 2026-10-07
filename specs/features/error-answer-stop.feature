Feature: A run stops at the first answer of claude that carries the error flag
  A refused key or a spent limit costs no Redo, and the issue names the cause.

  Background:
    Given the repo "factory-scratch" of the owner "sbiondolillo", the issue 12 and the token "token"
    And the run id "a1b2c3d4", the base branch "main" and an empty list of secret names
    And the plan model "plan-model" with turn limit 20, the build model "build-model" with turn limit 60 and the review model "review-model" with turn limit 30
    And the author name "Factory" and the author email "factory@example.com"
    And the clone holds the four paths of the layout and its head commit is "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And git reports a change in the work tree after each build
    And claude answers each prompt with the error flag clear
    And each dotnet command exits with the code 0 and the TRX file holds one passed test
    And the agent approves on the review prompt
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-1
  Scenario: An error on the plan prompt stops a plain run
    Given claude answers the plan prompt with the error flag set
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | plan prompt |


  @bullet-2
  Scenario: An error on the first build prompt stops a plain run
    Given claude answers the first build prompt with the error flag set
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | plan prompt |
      | build prompt |


  @bullet-3
  Scenario: An error on the first review prompt stops a plain run
    Given claude answers the first review prompt with the error flag set
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | plan prompt |
      | build prompt |
      | review prompt |


  @bullet-4
  Scenario: An error on the first build prompt stops a cleanup run
    Given claude answers the first build prompt with the error flag set
    And the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | build prompt |


  @bullet-5
  Scenario: An error on the first review prompt stops a cleanup run
    Given claude answers the first review prompt with the error flag set
    And the issue has the label "cleanup", the title "Fix the install test" and a body with the scope "README.md" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | build prompt |
      | review prompt |


  @bullet-6
  Scenario: An error on the plan prompt stops a refactor run
    Given claude answers the plan prompt with the error flag set
    And the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | plan prompt |


  @bullet-7
  Scenario: An error on the first build prompt stops a refactor run
    Given claude answers the first build prompt with the error flag set
    And the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | plan prompt |
      | build prompt |


  @bullet-8
  Scenario: An error on the first review prompt stops a refactor run
    Given claude answers the first review prompt with the error flag set
    And the issue has the label "refactor", the title "Rename Greeting" and a body with the first fenced block under "## Check" holding the line "test -f src/Core/Greeting.cs"
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then FactoryRun runs these prompts in this order
      | prompt |
      | plan prompt |
      | build prompt |
      | review prompt |


  @bullet-9
  Scenario: A stopped run gives back no pull request and no Redo
    Given claude answers the first build prompt with the error flag set
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    # run: printf '0'
    Then FactoryRun gives back a result with no pull request and 0 Redos


  @bullet-10
  Scenario: The comment holds the result text
    Given claude answers the first build prompt with the error flag set and the result text "Invalid API key"
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    # run: printf 'Invalid API key'
    Then FactoryRun posts one comment on the issue that holds "Invalid API key"


  @bullet-11
  Scenario: Standard output holds the comment body
    Given claude answers the first build prompt with the error flag set
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    Then standard output holds the body of the comment that FactoryRun posts on the issue


  @bullet-12
  Scenario: An error with no text gets a fixed sentence
    Given claude answers the first build prompt with the error flag set and no result text
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the agent writes the feature file and the plan on the plan prompt
    When a caller starts a run
    # run: printf 'claude answered with an error and no text.'
    Then FactoryRun posts one comment on the issue that holds "claude answered with an error and no text."


  @bullet-13
  Scenario: A plan error with no text sends one prompt
    Given the agent on the first run writes the sound feature file and the plan
    And claude answers the first run with the error flag set and no result text
    When I run the plan phase
    Then the plan phase ran one prompt

  @bullet-14
  Scenario: A plan error with no text gives a failed result
    Given the agent on the first run writes the sound feature file and the plan
    And claude answers the first run with the error flag set and no result text
    When I run the plan phase
    Then the result is failed
