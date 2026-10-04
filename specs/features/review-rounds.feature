Feature: A run answers each round of review threads until a review leaves none

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  @bullet-1
  Scenario: run help lists the review rounds option
    When the program starts with the arguments "run --help"
    # run: printf -- '--repo\n--issue\n--pass-env\n--timeout\n--review-rounds\n60\n3'
    Then standard output holds "--repo", "--issue", "--pass-env", "--timeout", "--review-rounds", "60" and "3"
    And GitHub gets no request
    # run: printf '0'
    And the exit code is 0

  @bullet-2
  Scenario: review rounds below 0
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --review-rounds -1"
    # run: printf '%s\n' --review-rounds
    Then standard error is one line that holds "--review-rounds"
    And GitHub gets no request
    # run: printf '1'
    And the exit code is 1

  @bullet-3
  Scenario: a changed head starts a second feedback run
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run changes the head commit of the pull request
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the run starts a second feedback run

  @bullet-4
  Scenario: no fourth feedback run by default
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run changes the head commit of the pull request
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '3'
    Then the run starts 3 feedback runs
    # run: printf '1'
    And the exit code is 1

  @bullet-5
  Scenario: review rounds 1 stops after one feedback run
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run changes the head commit of the pull request
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --review-rounds 1"
    # run: printf '1'
    Then the run starts 1 feedback runs
    # run: printf '1'
    And the exit code is 1

  @bullet-6
  Scenario: review rounds 0 starts no feedback run
    Given GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --review-rounds 0"
    Then the run starts no feedback run
    # run: printf '1'
    And the exit code is 1

  @bullet-7
  Scenario: an unchanged head starts no further feedback run
    Given GitHub answers each read of the review threads with the thread A
    And each feedback run leaves the head commit of the pull request as it was
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '1'
    Then the run starts 1 feedback runs
    # run: printf '1'
    And the exit code is 1

  @bullet-8
  Scenario: no unresolved thread and green checks ends the run
    Given GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the run starts no feedback run
    # run: printf 'checks: green, unresolved threads: 0\n0'
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0
