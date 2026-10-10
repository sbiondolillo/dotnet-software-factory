Feature: A run answers each round of review threads until a review leaves none

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run help lists the review rounds option
    When the program starts with the arguments "run --help"
    Then standard output holds "--repo", "--issue", "--pass-env", "--timeout", "--review-rounds", "60" and "3"
    And GitHub gets no request
    And the exit code is 0

  Scenario: review rounds below 0
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --review-rounds -1"
    Then standard error is one line that holds "--review-rounds"
    And GitHub gets no request
    And the exit code is 1

  Scenario: a changed head starts a second feedback run
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run changes the head commit of the pull request
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the run starts a second feedback run

  Scenario: no fourth feedback run by default
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run changes the head commit of the pull request
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the run starts 3 feedback runs
    And the exit code is 1

  Scenario: review rounds 1 stops after one feedback run
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run changes the head commit of the pull request
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --review-rounds 1"
    Then the run starts 1 feedback runs
    And the exit code is 1

  Scenario: review rounds 0 starts no feedback run
    Given GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --review-rounds 0"
    Then the run starts no feedback run
    And the exit code is 1

  Scenario: an unchanged head starts no further feedback run
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run leaves the head commit of the pull request as it was
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the run starts 1 feedback runs
    And the exit code is 1

  Scenario: no unresolved thread and green checks ends the run
    Given GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the run starts no feedback run
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0
