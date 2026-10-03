Feature: The console program writes one line to standard output as each step of a run starts

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  @bullet-1
  Scenario: run lines in order
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds exactly these lines in this order
      | line                                                |
      | clone: start                                        |
      | sandbox: start                                      |
      | plan: start                                         |
      | plan: passed                                        |
      | build: start                                        |
      | lint: start                                         |
      | test: start                                         |
      | test: passed                                        |
      | review: start                                       |
      | review: approved                                    |
      | deliver: start                                      |
      | https://github.com/sbiondolillo/factory-scratch/pull/10 |
      | watch: start                                        |
      | checks: green, unresolved threads: 0                |

  @bullet-2
  Scenario: sandbox line before docker image inspect
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'image inspect\nsandbox: start'
    Then when docker gets the command "image inspect" standard output already holds the line "sandbox: start"

  @bullet-3
  Scenario: plan line before the plan prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'plan: start'
    Then when claude gets the plan prompt standard output already holds the line "plan: start"

  @bullet-4
  Scenario: layout failure
    Given the clone lacks Solution.slnx
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'layout: failed\n12'
    Then standard output holds the line "layout: failed"
    And after that line standard output holds the body of the comment that GitHub gets on the issue 12 and that body ends standard output

  @bullet-5
  Scenario: plan failure
    Given the agent writes only ".factory/plan.md" on the plan prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'plan: failed\n12'
    Then standard output holds the line "plan: failed"
    And after that line standard output holds the body of the comment that GitHub gets on the issue 12 and that body ends standard output

  @bullet-6
  Scenario: red test gate then redo
    Given the test gate is red after the first build and green after the second
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'test: failed\nredo: 1\nbuild: start'
    Then standard output holds the lines "test: failed", "redo: 1", "build: start" in this order with no line between them

  @bullet-7
  Scenario: blocked review then redo
    Given the agent writes a blocked review on the first review prompt
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'review: blocked\nredo: 1\nbuild: start'
    Then standard output holds the lines "review: blocked", "redo: 1", "build: start" in this order with no line between them

  @bullet-8
  Scenario: Redos spent
    Given the test gate is red after each of four builds
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'redo: 1\nredo: 2\nredo: 3'
    Then standard output holds each of the lines "redo: 1", "redo: 2" and "redo: 3" one time
    # run: printf 'test: failed\n12'
    And after the last line "test: failed" standard output holds the body of the comment that GitHub gets on the issue 12 and that body ends standard output

  @bullet-9
  Scenario: feedback line once in a run
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'feedback: start\nwatch: start'
    Then standard output holds the line "feedback: start" one time, between the first and the second line "watch: start"

  @bullet-10
  Scenario: feedback lines in order
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then standard output holds exactly these lines in this order
      | line                                 |
      | feedback: start                      |
      | clone: start                         |
      | sandbox: start                       |
      | build: start                         |
      | lint: start                          |
      | test: start                          |
      | test: passed                         |
      | review: start                        |
      | review: approved                     |
      | watch: start                         |
      | checks: green, unresolved threads: 0 |

  @bullet-11
  Scenario: feedback Redos spent
    Given GitHub answers each read of the review threads with the thread A
    And the test gate is red after each of four builds
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf '10\ntest: failed\nwatch: start'
    Then standard output holds the body of the comment that GitHub gets on the pull request 10 between the last line "test: failed" and the line "watch: start"

  @bullet-12
  Scenario: feedback with no threads
    Given GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then standard output holds exactly these lines in this order
      | line                                 |
      | feedback: start                      |
      | watch: start                         |
      | checks: green, unresolved threads: 0 |
