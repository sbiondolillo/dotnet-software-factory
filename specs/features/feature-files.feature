Feature: FeatureFiles reads scenario titles and computes Reqnroll test names

  Scenario: Two scenarios in a feature file
    Given a feature file "greetings.feature" with the lines:
      | line                                  |
      | Feature: Greetings                    |
      | Scenario: The greeter greets by name  |
      | Given a name                          |
      | Scenario: The greeter greets twice    |
      | Given a name                          |
    When FeatureFiles reads the scenario titles of "greetings.feature"
    Then FeatureFiles returns the titles "The greeter greets by name" and "The greeter greets twice" in that order

  Scenario: A Background contributes no title
    Given a feature file "background.feature" with the lines:
      | line                                  |
      | Feature: Background feature           |
      | Background:                           |
      | Given a common setup                  |
      | Scenario: The greeter greets by name  |
      | Given a name                          |
    When FeatureFiles reads the scenario titles of "background.feature"
    Then FeatureFiles returns the one title "The greeter greets by name"

  Scenario: A Scenario Outline is one scenario regardless of its example rows
    Given a feature file "outline.feature" with the lines:
      | line                                    |
      | Feature: Outline feature                |
      | Scenario Outline: The greeter greets <who> |
      | Given a name "<who>"                    |
      | Examples:                               |
      | who                                     |
      | Ada                                     |
      | Grace                                   |
    When FeatureFiles reads the scenario titles of "outline.feature"
    Then FeatureFiles returns the one title "The greeter greets <who>"

  Scenario: A scenario under a Rule is a scenario of the file
    Given a feature file "rule.feature" with the lines:
      | line                                   |
      | Feature: Rule feature                  |
      | Scenario: The greeter greets by name   |
      | Given a name                           |
      | Rule: Greeting rules                   |
      | Scenario: The greeter greets by rule   |
      | Given a name                           |
    When FeatureFiles reads the scenario titles of "rule.feature"
    Then FeatureFiles returns the titles "The greeter greets by name" and "The greeter greets by rule" in that order

  Scenario: A feature file with no text has no scenarios
    Given a feature file "empty.feature" with no content
    When FeatureFiles reads the scenario titles of "empty.feature"
    Then FeatureFiles returns no titles

  Scenario: A directory scan finds every feature file at any depth and skips other files
    Given a feature file "a.feature" with the lines:
      | line                        |
      | Feature: A feature          |
      | Scenario: A scenario in a   |
      | Given a name                |
    And a feature file "sub/c.feature" with the lines:
      | line                        |
      | Feature: C feature          |
      | Scenario: A scenario in c   |
      | Given a name                |
    And a file "notes.txt" with the content:
      """
      not a feature file
      """
    When FeatureFiles reads the scenario titles of the directory
    Then FeatureFiles returns "a.feature" with the one title "A scenario in a" and "sub/c.feature" with the one title "A scenario in c"
    And FeatureFiles reads only those two files

  Scenario: An unparsable feature file throws a FeatureFileException
    Given a feature file "invalid.feature" with the content:
      """
      This is not a feature file.
      """
    When FeatureFiles reads the scenario titles of "invalid.feature"
    Then FeatureFiles throws a FeatureFileException naming the path "invalid.feature"
    And the exception message holds the parser's message

  Scenario: The test name for a plain title
    Given the scenario title "The greeter greets by name"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "TheGreeterGreetsByName"

  Scenario: The test name for a title with a hyphenated word
    Given the scenario title "A per-location greeting"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "APer_LocationGreeting"

  Scenario: The test name for a title with a dotted version number
    Given the scenario title "The branch is v1.2"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "TheBranchIsV1_2"

  Scenario: The test name for a title starting with a digit and a comma
    Given the scenario title "3 retries, then the run stops"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "_3RetriesThenTheRunStops"

  Scenario: The test name for a title with an apostrophe
    Given the scenario title "The checkout's branch is new"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "TheCheckoutsBranchIsNew"

  Scenario: The test name for a title with quoted words
    Given the scenario title 'The "new" branch is ready'
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "TheNewBranchIsReady"

  Scenario: The test name for a title with a number and parentheses
    Given the scenario title "An HTTP 404 answer (not found) fails"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "AnHTTP404AnswerNotFoundFails"

  Scenario: The test name for a title with an environment variable name
    Given the scenario title "the token is FACTORY_GITHUB_TOKEN"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "TheTokenIsFACTORY_GITHUB_TOKEN"

  Scenario: The test name for a title with accented letters
    Given the scenario title "Café résumé is read"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "CafeResumeIsRead"

  Scenario: The test name for a title with a placeholder
    Given the scenario title "The greeter greets <who>"
    When FeatureFiles computes the test name
    Then FeatureFiles returns the test name "TheGreeterGreetsWho"

  Scenario: A feature file that is a symbolic link throws FeatureFileException with no text of its target
    Given a file "environ" with the content:
      """
      FACTORY_GITHUB_TOKEN=s3cret
      """
    And "leak.feature" is a symbolic link to the file "environ"
    When FeatureFiles reads the scenario titles of "leak.feature"
    Then FeatureFiles throws a FeatureFileException that holds "leak.feature" and lacks "s3cret"
