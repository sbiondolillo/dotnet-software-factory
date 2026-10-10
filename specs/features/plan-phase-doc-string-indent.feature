Feature: Plan phase doc string indent

  @bullet-1
  Scenario: A doc string indented past its delimiter fails with its line
    Given the agent on every run writes the plan
    And the agent on every run writes the file "specs/features/config.feature" with the text
      ```
      Feature: Config

        Scenario: The config holds two keys
          Given the config
            """
            name: app
              port: 80
            """
          When the app starts
          Then the config
            """
              name: app

              port: 80
            """
      ```
    When I run the plan phase
    Then the result is failed
    And the plan phase reports one failure
    And the failure names "specs/features/config.feature", "line 11" and "the column of its opening delimiter"

  @bullet-2
  Scenario: A backtick doc string indented past its delimiter fails with its line
    Given the agent on every run writes the plan
    And the agent on every run writes the file "specs/features/ticks.feature" with the text
      """
      Feature: Ticks

        Scenario: The note is indented
          Given the note
            ```
            flat text
            ```
          Then the note
            ```
              deep text
            ```
      """
    When I run the plan phase
    Then the result is failed
    And the plan phase reports one failure
    And the failure names "specs/features/ticks.feature", "line 9" and "the column of its opening delimiter"
