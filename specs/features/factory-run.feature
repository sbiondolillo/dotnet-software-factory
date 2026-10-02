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

  @bullet-1
  Scenario: The run clones the repo
    When a caller starts a run
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git'
    Then git clones "https://github.com/sbiondolillo/factory-scratch.git" into the directory that the caller gave

  @bullet-2
  Scenario: The plan prompt holds the issue
    When a caller starts a run
    # run: printf 'plan-model\n20\nAdd a checkout total\nThe checkout shows the total of the cart.'
    Then FactoryRun runs one prompt with the model "plan-model" and the turn limit 20 that holds "Add a checkout total" and "The checkout shows the total of the cart."

  @bullet-3
  Scenario: The build prompt follows a passing plan phase
    When a caller starts a run
    # run: printf 'build-model\n60'
    Then FactoryRun runs one prompt with the model "build-model" and the turn limit 60

  @bullet-4
  Scenario: The review prompt follows a passing test gate
    When a caller starts a run
    # run: printf 'review-model\n30'
    Then FactoryRun runs one prompt with the model "review-model" and the turn limit 30

  @bullet-5
  Scenario: The review prompt holds the base commit
    When a caller starts a run
    # run: printf '4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c'
    Then the review prompt holds "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"

  @bullet-6
  Scenario: A clone without Solution.slnx gets a comment
    Given the clone lacks "Solution.slnx"
    When a caller starts a run
    # run: printf 'Solution.slnx'
    Then FactoryRun posts one comment on the issue that holds "Solution.slnx"

  @bullet-7
  Scenario: A clone without specs/features/ gets a comment
    Given the clone lacks "specs/features/"
    When a caller starts a run
    # run: printf 'specs/features/'
    Then FactoryRun posts one comment on the issue that holds "specs/features/"

  @bullet-8
  Scenario: A clone without tests/Specs/ gets a comment
    Given the clone lacks "tests/Specs/"
    When a caller starts a run
    # run: printf 'tests/Specs/'
    Then FactoryRun posts one comment on the issue that holds "tests/Specs/"

  @bullet-9
  Scenario: A clone without tests/Integration/ gets a comment
    Given the clone lacks "tests/Integration/"
    When a caller starts a run
    # run: printf 'tests/Integration/'
    Then FactoryRun posts one comment on the issue that holds "tests/Integration/"

  @bullet-10
  Scenario: A clone without two paths gets one comment naming both
    Given the clone lacks "specs/features/"
    And the clone lacks "tests/Integration/"
    When a caller starts a run
    # run: printf 'specs/features/\ntests/Integration/'
    Then FactoryRun posts one comment on the issue that holds "specs/features/" and "tests/Integration/"

  @bullet-11
  Scenario: A clone without Solution.slnx starts no container
    Given the clone lacks "Solution.slnx"
    When a caller starts a run
    Then FactoryRun starts no container and runs no prompt

  @bullet-12
  Scenario: A clone without Solution.slnx gives no pull request
    Given the clone lacks "Solution.slnx"
    When a caller starts a run
    # run: printf '0'
    Then FactoryRun gives back a result with no pull request and 0 Redos

  @bullet-13
  Scenario: A plan phase that writes no plan gets a comment
    Given the agent writes no ".factory/plan.md" on the plan prompt and on its correction
    When a caller starts a run
    # run: printf '.factory/plan.md'
    Then FactoryRun posts one comment on the issue that holds ".factory/plan.md"

  @bullet-14
  Scenario: A plan phase that writes no plan ends the run
    Given the agent writes no ".factory/plan.md" on the plan prompt and on its correction
    When a caller starts a run
    Then FactoryRun runs no build prompt
    # run: printf '0'
    And FactoryRun gives back a result with no pull request and 0 Redos

  @bullet-15
  Scenario: An approved build is committed
    When a caller starts a run
    # run: printf 'Add a checkout total\nFactory\nfactory@example.com'
    Then git commits the change of the build with the message "Add a checkout total", the author name "Factory" and the author email "factory@example.com"

  @bullet-16
  Scenario: An approved build is pushed
    When a caller starts a run
    # run: printf 'factory/a1b2c3d4'
    Then git pushes the branch "factory/a1b2c3d4"

  @bullet-17
  Scenario: An approved build opens a pull request
    When a caller starts a run
    # run: printf 'sbiondolillo/factory-scratch\nfactory/a1b2c3d4\nmain\nAdd a checkout total'
    Then FactoryRun opens a pull request on "sbiondolillo/factory-scratch" from the branch "factory/a1b2c3d4" into "main" with the title "Add a checkout total"

  @bullet-18
  Scenario: An approved build gives back the pull request
    When a caller starts a run
    # run: printf '10\nhttps://github.com/sbiondolillo/factory-scratch/pull/10\n0'
    Then FactoryRun gives back a result with the pull request of the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10", and 0 Redos

  @bullet-19
  Scenario: The secret names reach the integration command
    Given the run's list of secret names is "WEATHER_API_KEY" and "MAPS_API_KEY"
    When a caller starts a run
    # run: printf 'WEATHER_API_KEY\nMAPS_API_KEY\ndotnet run --no-restore --project tests/Integration'
    Then docker gives the names "WEATHER_API_KEY" and "MAPS_API_KEY" to the command "dotnet run --no-restore --project tests/Integration"

  @bullet-20
  Scenario: An error flag on the build answer passes no gate
    Given claude answers the build prompt with the error flag set
    When a caller starts a run
    # run: printf '10\n0'
    Then FactoryRun gives back a result with the pull request of the number 10 and 0 Redos

  @bullet-21
  Scenario: A red test gate feeds the next build
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a run
    # run: printf 'Assert.Equal() Failure: Values differ'
    Then FactoryRun runs a second build prompt that holds "Assert.Equal() Failure: Values differ"

  @bullet-22
  Scenario: A red test gate sends no review
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a run
    # run: printf 'review-model'
    Then FactoryRun runs the second build prompt before any prompt with the model "review-model"

  @bullet-23
  Scenario: A red test gate spends one Redo
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a run
    # run: printf '10\n1'
    Then FactoryRun gives back a result with the pull request of the number 10 and 1 Redo

  @bullet-24
  Scenario: A rejecting review feeds the next build
    Given the review rejects the first build and approves the second build
    When a caller starts a run
    # run: printf 'The total ignores the second item of the cart.'
    Then FactoryRun runs a second build prompt that holds "The total ignores the second item of the cart."

  @bullet-25
  Scenario: A rejecting review spends one Redo
    Given the review rejects the first build and approves the second build
    When a caller starts a run
    # run: printf '10\n1'
    Then FactoryRun gives back a result with the pull request of the number 10 and 1 Redo

  @bullet-26
  Scenario: Mixed redos share one budget
    Given the test gate is red after the first build
    And the review rejects the second build
    And the test gate is red after the third build
    And the review approves the fourth build
    When a caller starts a run
    # run: printf '10\n3'
    Then FactoryRun gives back a result with the pull request of the number 10 and 3 Redos

  @bullet-27
  Scenario: A run has four builds at most
    Given the test gate is red after each of four builds
    When a caller starts a run
    # run: printf '4'
    Then FactoryRun runs 4 build prompts in the run

  @bullet-28
  Scenario: A spent budget posts the evidence
    Given the test gate is red after each of four builds
    When a caller starts a run
    # run: printf 'Assert.Equal() Failure: Values differ'
    Then FactoryRun posts one comment on the issue that holds "Assert.Equal() Failure: Values differ"

  @bullet-29
  Scenario: A spent budget pushes nothing
    Given the test gate is red after each of four builds
    When a caller starts a run
    Then git pushes no branch
    # run: printf '3'
    And FactoryRun gives back a result with no pull request and 3 Redos

  @bullet-30
  Scenario: A spent budget after a rejecting review posts the blocking items
    Given the test gate is red after the first build and the third build
    And the review rejects the second build and the fourth build
    When a caller starts a run
    # run: printf 'The total ignores the second item of the cart.\n3'
    Then FactoryRun posts one comment on the issue that holds "The total ignores the second item of the cart." and gives back a result with no pull request and 3 Redos

  @bullet-31
  Scenario: A comment over the limit is cut
    Given the test gate is red after each of four builds
    And the test command writes the letter "a" 300000 times to its standard output
    When a caller starts a run
    # run: printf '262144\na\n200000\n[cut]'
    Then FactoryRun posts one comment of 262144 bytes or fewer that holds the letter "a" 200000 times in sequence and "[cut]"

  @bullet-32
  Scenario: The container is removed after the delivery
    When a caller starts a run
    Then FactoryRun removes the container after the delivery

  @bullet-33
  Scenario: A refused pull request throws and removes the container
    Given GitHub answers the open of the pull request with the status code 422
    When a caller starts a run
    Then FactoryRun throws GitHubRequestException and removes the container
