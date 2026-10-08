Feature: The plan phase refuses a doc string whose text sits deeper than its opening delimiter

  Background:
    Given an empty work tree
    And the issue has the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the model is "claude-haiku-4-5-20251001" and the turn limit is 40

  @bullet-1
  Scenario: A doc string with a line deeper than its delimiter fails naming the file and the line
    Given the agent on every run writes the plan
    And the agent on every run writes the file "specs/features/config.feature" with the lines
      | indent | text |
      | 0 | Feature: Config |
      | 0 | |
      | 2 | Scenario: The config holds two keys |
      | 4 | Given the config |
      | 6 | """ |
      | 6 | name: app |
      | 8 | port: 80 |
      | 6 | """ |
      | 4 | When the app starts |
      | 4 | Then the config |
      | 6 | """ |
      | 8 | name: app |
      | 6 | """ |
    When I run the plan phase
    Then the result is failed
    # run: printf '1'
    And the result holds 1 failure
    # run: printf '%s\n' 'specs/features/config.feature' 'line 11' 'the column of its opening delimiter'
    And the failure holds "specs/features/config.feature", "line 11" and "the column of its opening delimiter"

  @bullet-2
  Scenario: A backtick doc string with text deeper than its delimiter fails naming the file and the line
    Given the agent on every run writes the plan
    And the agent on every run writes the file "specs/features/ticks.feature" with the lines
      | indent | text |
      | 0 | Feature: Ticks |
      | 0 | |
      | 2 | Scenario: The note is indented |
      | 4 | Given the note |
      | 6 | ``` |
      | 8 | deep text |
      | 6 | ``` |
    When I run the plan phase
    Then the result is failed
    # run: printf '1'
    And the result holds 1 failure
    # run: printf '%s\n' 'specs/features/ticks.feature' 'line 5' 'the column of its opening delimiter'
    And the failure holds "specs/features/ticks.feature", "line 5" and "the column of its opening delimiter"

  @bullet-3
  Scenario: The correction prompt names the file, the line and the rule
    Given the agent on the first run writes the plan
    And the agent on the first run writes the file "specs/features/note.feature" with the lines
      | indent | text |
      | 0 | Feature: Note |
      | 0 | |
      | 2 | Scenario: The note is indented |
      | 4 | Given the note |
      | 6 | """ |
      | 8 | deep text |
      | 6 | """ |
    When I run the plan phase
    # run: printf '%s\n' 'specs/features/note.feature' 'line 5' 'the column of its opening delimiter'
    Then the second prompt holds "specs/features/note.feature", "line 5" and "the column of its opening delimiter"
