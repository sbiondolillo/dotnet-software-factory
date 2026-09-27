Feature: FeatureFiles reads scenario titles from a feature file or directory

  The factory asks FeatureFiles for the scenario titles of a `.feature` file or of every
  `.feature` file under a directory, so it can match a test report's test names back to
  the scenarios it wrote.

  @bullet-1
  Scenario: Reading gives every scenario title of a file in order
    Given a feature file with the scenarios "The greeter greets by name" and "The greeter greets twice"
    When I read the scenario titles of that file with FeatureFiles
    # run: printf 'The greeter greets by name\nThe greeter greets twice'
    Then FeatureFiles returns the titles "The greeter greets by name" and "The greeter greets twice" in that order

  @bullet-2
  Scenario: A Background is not a scenario
    Given a feature file with a Background and the scenario "The greeter greets by name"
    When I read the scenario titles of that file with FeatureFiles
    # run: printf 'The greeter greets by name'
    Then FeatureFiles returns the one title "The greeter greets by name"

  @bullet-3
  Scenario: A Scenario Outline is one scenario with one title, whatever the number of example rows
    Given a feature file with an outline titled "The greeter greets <who>" and two example rows
    When I read the scenario titles of that file with FeatureFiles
    Then FeatureFiles returns the one title "The greeter greets <who>"

  @bullet-4
  Scenario: A scenario under a Rule is a scenario of the file
    Given a feature file with the scenario "The greeter greets by name" and, under a rule, the scenario "The greeter greets by rule"
    When I read the scenario titles of that file with FeatureFiles
    # run: printf 'The greeter greets by name\nThe greeter greets by rule'
    Then FeatureFiles returns the titles "The greeter greets by name" and "The greeter greets by rule" in that order

  @bullet-5
  Scenario: A feature file with no text has no scenarios
    Given an empty feature file
    When I read the scenario titles of that file with FeatureFiles
    Then FeatureFiles returns an empty list of titles

  @bullet-6
  Scenario: Reading a directory reads only the feature files under it
    Given a directory containing the feature file "a.feature" with the scenario "The greeter greets by name"
    And the directory containing the feature file "sub/c.feature" with the scenario "The greeter greets by rule"
    And the directory containing the file "notes.txt"
    When I read the scenario titles of that directory with FeatureFiles
    # run: printf 'a.feature\nsub/c.feature'
    Then FeatureFiles returns the titles of "a.feature" and "sub/c.feature", and reads only those two files

  @bullet-7
  Scenario: A file the Gherkin parser refuses throws a FeatureFileException
    Given a feature file containing invalid Gherkin text
    When I read the scenario titles of that file with FeatureFiles
    Then FeatureFiles throws a FeatureFileException whose message names the path of the file and holds the parser's message
