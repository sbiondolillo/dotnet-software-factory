Feature: A run stops at the first answer of claude that carries the error flag

  Background:
    Given the repo "factory-scratch" of the owner "sbiondolillo", the issue 12 and the token "token"
    And the run id "a1b2c3d4", the base branch "main" and an empty list of secret names
    And the plan model "plan-model" with turn limit 20, the build model "build-model" with turn limit 60 and the review model "review-model" with turn limit 30
    And the author name "Factory" and the author email "factory@example.com"
    And GitHub answers the read of the issue with the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the clone holds the four paths of the layout and its head commit is "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And git reports a change in the work tree after each build
    And the agent writes the feature file and the plan on the plan prompt
    And claude answers each prompt with the error flag clear
    And each dotnet command exits with the code 0 and the TRX file holds one passed test
    And the agent approves on the review prompt
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-1
  Scenario: An error answer to the plan prompt ends the run at the plan prompt
    Given claude answers the plan prompt with the error flag set
    When a caller starts a run
    Then the plan prompt is the last docker exec command of the run

  @bullet-2
  Scenario: An error answer to the first build prompt ends the run at the build prompt
    Given claude answers the first build prompt with the error flag set
    When a caller starts a run
    Then the build prompt is the last docker exec command of the run

  @bullet-3
  Scenario: An error answer to the first review prompt ends the run at the review prompt
    Given claude answers the first review prompt with the error flag set
    When a caller starts a run
    Then the review prompt is the last docker exec command of the run

  @bullet-9
  Scenario: A stopped run gives back no pull request and no Redos
    Given claude answers the first build prompt with the error flag set
    When a caller starts a run
    # run: printf 0
    Then FactoryRun gives back a result with no pull request and 0 Redos

  @bullet-10
  Scenario: The comment holds the result text of the answer
    Given claude answers the first build prompt with the error flag set and the result text "Invalid API key"
    When a caller starts a run
    # run: printf 'Invalid API key'
    Then FactoryRun posts one comment on the issue that holds "Invalid API key"

  @bullet-11
  Scenario: Standard output holds the body of the comment
    Given claude answers the first build prompt with the error flag set and the result text "Invalid API key"
    When a caller starts a run
    Then standard output holds the body of the comment that the run posts on the issue

  @bullet-12
  Scenario: An error answer with no result text gets the fixed sentence
    Given claude answers the first build prompt with the error flag set and no result text
    When a caller starts a run
    # run: printf 'claude answered with an error and no text.'
    Then FactoryRun posts one comment on the issue that holds "claude answered with an error and no text."
