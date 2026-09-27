Feature: FeatureFiles maps a scenario title to Reqnroll's test name

  The factory asks FeatureFiles for the test method name Reqnroll generates for a
  scenario title, by calling Reqnroll's own `ToIdentifier`, so a test report's method
  name always matches back to the scenario that pins it.

  @bullet-8
  Scenario: The test name for "The greeter greets by name"
    Given the scenario title "The greeter greets by name"
    When I compute the test name with FeatureFiles
    # run: printf 'TheGreeterGreetsByName'
    Then FeatureFiles returns the test name "TheGreeterGreetsByName"

  @bullet-9
  Scenario: The test name for "A per-location greeting"
    Given the scenario title "A per-location greeting"
    When I compute the test name with FeatureFiles
    # run: printf 'APer_LocationGreeting'
    Then FeatureFiles returns the test name "APer_LocationGreeting"

  @bullet-10
  Scenario: The test name for "The branch is v1.2"
    Given the scenario title "The branch is v1.2"
    When I compute the test name with FeatureFiles
    # run: printf 'TheBranchIsV1_2'
    Then FeatureFiles returns the test name "TheBranchIsV1_2"

  @bullet-11
  Scenario: The test name for "3 retries, then the run stops"
    Given the scenario title "3 retries, then the run stops"
    When I compute the test name with FeatureFiles
    # run: printf '_3RetriesThenTheRunStops'
    Then FeatureFiles returns the test name "_3RetriesThenTheRunStops"

  @bullet-12
  Scenario: The test name for "The checkout's branch is new"
    Given the scenario title "The checkout's branch is new"
    When I compute the test name with FeatureFiles
    # run: printf 'TheCheckoutsBranchIsNew'
    Then FeatureFiles returns the test name "TheCheckoutsBranchIsNew"

  @bullet-13
  Scenario: The test name for a title with a quoted word
    Given the scenario title 'The "new" branch is ready'
    When I compute the test name with FeatureFiles
    # run: printf 'TheNewBranchIsReady'
    Then FeatureFiles returns the test name "TheNewBranchIsReady"

  @bullet-14
  Scenario: The test name for "An HTTP 404 answer (not found) fails"
    Given the scenario title "An HTTP 404 answer (not found) fails"
    When I compute the test name with FeatureFiles
    # run: printf 'AnHTTP404AnswerNotFoundFails'
    Then FeatureFiles returns the test name "AnHTTP404AnswerNotFoundFails"

  @bullet-15
  Scenario: The test name for "the token is FACTORY_GITHUB_TOKEN"
    Given the scenario title "the token is FACTORY_GITHUB_TOKEN"
    When I compute the test name with FeatureFiles
    # run: printf 'TheTokenIsFACTORY_GITHUB_TOKEN'
    Then FeatureFiles returns the test name "TheTokenIsFACTORY_GITHUB_TOKEN"

  @bullet-16
  Scenario: The test name for a title with accented letters
    Given the scenario title "Café résumé is read"
    When I compute the test name with FeatureFiles
    # run: printf 'CafeResumeIsRead'
    Then FeatureFiles returns the test name "CafeResumeIsRead"

  @bullet-17
  Scenario: The test name for a title with an Outline placeholder
    Given the scenario title "The greeter greets <who>"
    When I compute the test name with FeatureFiles
    # run: printf 'TheGreeterGreetsWho'
    Then FeatureFiles returns the test name "TheGreeterGreetsWho"
