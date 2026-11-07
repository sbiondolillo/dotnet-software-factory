Feature: Factory run
  A factory run takes one issue of a target repo to a pull request within three Redos.

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

  Scenario: The run clones the repo
    When a caller starts a run
    Then git clones "https://github.com/sbiondolillo/factory-scratch.git" into the directory that the caller gave

  Scenario: The plan prompt holds the issue
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "plan-model" and the turn limit 20 that holds "Add a checkout total" and "The checkout shows the total of the cart."

  Scenario: The build prompt follows a passing plan phase
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "build-model" and the turn limit 60

  Scenario: The review prompt follows a passing test gate
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "review-model" and the turn limit 30

  Scenario: The review prompt holds the base commit
    When a caller starts a run
    Then the review prompt holds "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"

  Scenario: The pull request body names the head commit of the clone as the base commit
    When a caller starts a run
    Then the body of the pull request holds the line "Base commit: 4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"

  Scenario: A run with a base commit checks it out in the clone after the clone
    Given the caller gives the base commit "9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c"
    When a caller starts a run
    Then git runs "checkout --detach 9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c" in the directory that the caller gave, after the clone and before the head read

  Scenario: A run with a base commit holds it in the review prompt and in the pull request body
    Given the caller gives the base commit "9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c"
    When a caller starts a run
    Then the review prompt holds "9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c"
    And the body of the pull request holds the line "Base commit: 9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c"

  Scenario: A run with no base commit checks out no commit
    When a caller starts a run
    Then git runs no "checkout --detach"

  Scenario: A clone without Solution.slnx gets a comment
    Given the clone lacks "Solution.slnx"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "Solution.slnx"

  Scenario: A clone without specs/features/ gets a comment
    Given the clone lacks "specs/features/"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "specs/features/"

  Scenario: A clone without tests/Specs/ gets a comment
    Given the clone lacks "tests/Specs/"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "tests/Specs/"

  Scenario: A clone without tests/Integration/ gets a comment
    Given the clone lacks "tests/Integration/"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "tests/Integration/"

  Scenario: A clone without two paths gets one comment naming both
    Given the clone lacks "specs/features/"
    And the clone lacks "tests/Integration/"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "specs/features/" and "tests/Integration/"

  Scenario: A clone without Solution.slnx starts no container
    Given the clone lacks "Solution.slnx"
    When a caller starts a run
    Then FactoryRun starts no container and runs no prompt

  Scenario: A clone without Solution.slnx gives no pull request
    Given the clone lacks "Solution.slnx"
    When a caller starts a run
    Then FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A plan phase that writes no plan gets a comment
    Given the agent writes no ".factory/plan.md" on the plan prompt and on its correction
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds ".factory/plan.md"

  Scenario: A plan phase that writes no plan ends the run
    Given the agent writes no ".factory/plan.md" on the plan prompt and on its correction
    When a caller starts a run
    Then FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: An approved build is committed
    When a caller starts a run
    Then git commits the change of the build with the message "Add a checkout total", the author name "Factory" and the author email "factory@example.com"

  Scenario: An approved build is pushed
    When a caller starts a run
    Then git pushes the branch "factory/a1b2c3d4"

  Scenario: An approved build opens a pull request
    When a caller starts a run
    Then FactoryRun opens a pull request on "sbiondolillo/factory-scratch" from the branch "factory/a1b2c3d4" into "main" with the title "Add a checkout total"

  Scenario: An approved build gives back the pull request
    When a caller starts a run
    Then FactoryRun gives back a result with the pull request of the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10", and 0 Redos

  Scenario: The secret names reach the integration command
    Given the run's list of secret names is "WEATHER_API_KEY" and "MAPS_API_KEY"
    When a caller starts a run
    Then docker gives the names "WEATHER_API_KEY" and "MAPS_API_KEY" to the command "dotnet run --no-restore --project tests/Integration"

  Scenario: A spent budget posts the evidence
    Given the test gate is red after each of four builds
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "Assert.Equal() Failure: Values differ"

  Scenario: A spent budget pushes nothing
    Given the test gate is red after each of four builds
    When a caller starts a run
    Then git pushes no branch
    And FactoryRun gives back a result with no pull request and 3 Redos

  Scenario: A comment over the limit is cut
    Given the test gate is red after each of four builds
    And the test command writes the letter "a" 300000 times to its standard output
    When a caller starts a run
    Then FactoryRun posts one comment of 262144 bytes or fewer that holds the letter "a" 200000 times in sequence and "[cut]"

  Scenario: The container is removed after the delivery
    When a caller starts a run
    Then FactoryRun removes the container after the delivery

  Scenario: A refused pull request throws and removes the container
    Given GitHub answers the open of the pull request with the status code 422
    When a caller starts a run
    Then FactoryRun throws GitHubRequestException and removes the container

  Scenario: A stop of the session branch posts its text on the issue and delivers nothing
    Given the builder stops the run on the first build with the text "The session committed files under .factory/, which only the factory writes: .factory/x. The run stops."
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "The session committed files under .factory/, which only the factory writes: .factory/x. The run stops." and gives back a result with no pull request and 0 Redos
    And the run runs no command in the container
    And git pushes no branch "factory/a1b2c3d4"

  Scenario: A fault of a build answer reaches the next build prompt
    Given the first build answer carries the fault "The build pushed no commit to the session branch."
    And the test gate is red after the first build and passes after the second build
    When a caller starts a run
    Then FactoryRun runs a second build prompt that holds "The build pushed no commit to the session branch."

  Scenario: Every dotnet command of the run goes through docker exec in the container of the backend
    When a caller starts a run
    Then every dotnet command of the run goes through docker exec

  Scenario: The run hands the backend the session branch of the run, the head of the clone and the title of the issue
    When a caller starts a run
    Then the run hands the backend the session branch "factory/a1b2c3d4-session", the base commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and the title "Add a checkout total"
