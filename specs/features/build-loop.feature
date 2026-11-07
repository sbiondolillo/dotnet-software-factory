Feature: One build loop runs every recipe

  Scenario: The gates run in the order of the list, then the review
    Given a build loop with the gates scope, test, api and check
    When the build loop runs
    Then the loop called build, lint, scope, test, api, check, review in that order
    And the loop passed with 0 Redos

  Scenario: The first gate that fails ends the gates of that build
    Given a build loop with the gates scope, test, api and check
    And the gate test fails once with the Redo text "test failed: GreeterTests.Hello"
    When the build loop runs
    Then the loop called build, lint, scope, test, build, lint, scope, test, api, check, review in that order
    And the job summary holds the item "- Redo 1, after test 1: test failed: GreeterTests.Hello"
    And the loop passed with 1 Redos

  Scenario: A blocked review gets a Redo that names the review row
    Given a build loop with the gates test
    And the review blocks once with the Redo text "Bind the scenario Hello."
    When the build loop runs
    Then the job summary holds the item "- Redo 1, after review 1: Bind the scenario Hello."
    And the second build got the Redo text "Bind the scenario Hello."

  Scenario: The fourth rejection spends the budget of three Redos
    Given a build loop with the gates test
    And the review blocks every time with the Redo text "Still wrong."
    When the build loop runs
    Then the loop ran 4 builds
    And the loop spent its budget with 3 Redos
    And the comment of the loop is "The budget of 3 Redos is spent. The last build still fails:" and the text "Still wrong."

  Scenario: An error of the build stops the loop with its text
    Given a build loop with the gates test
    And the build answers with the error "The API answered 529."
    When the build loop runs
    Then the loop stopped with 0 Redos and the comment "The API answered 529."
    And the job summary holds the row "| build 1 | 5 | 0 | $0 | failed |"
    And the loop called build in that order
    And the progress lines are build: start, build: failed

  Scenario: An error of the review stops the loop with its text
    Given a build loop with the gates test
    And the review answers with the error "The API answered 500."
    When the build loop runs
    Then the loop stopped with 0 Redos and the comment "The API answered 500."
    And the job summary holds the row "| review 1 | 7 | 3 | $0.5 | failed |"
    And the progress lines are build: start, build: passed, lint: start, lint: passed, test: start, test: passed, review: start, review: failed

  Scenario: The lint runs after every build that passes
    Given a build loop with the gates test
    And the gate test fails once with the Redo text "test failed: GreeterTests.Hello"
    When the build loop runs
    Then the loop called build, lint, test, build, lint, test, review in that order

  Scenario: Each row writes its result line, and the lint has a row of its own
    Given a build loop with the gates test
    When the build loop runs
    Then the progress lines are build: start, build: passed, lint: start, lint: passed, test: start, test: passed, review: start, review: approved
    And the job summary holds the row "| build 1 | 5 | 0 | $0 | passed |"
    And the job summary holds the row "| lint 1 | 1 | 0 | $0 | passed |"
