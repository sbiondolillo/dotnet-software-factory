Feature: The factory pins the scenarios of an issue as the owner wrote them
  A repo owner writes the scenarios of a behaviour issue under ## Scenarios, and the build proves what the owner approved.

  Scenario: A whole-file block for a new feature file becomes that file in the pin
    Given an issue whose ## Scenarios holds a block with a Feature: line for "specs/features/greeting-name.feature"
    When the factory runs the issue
    Then the pin holds "specs/features/greeting-name.feature" with the hash of the block's text after its first line

  Scenario: A whole-file block for a feature file on main replaces that file
    Given the clone holds "specs/features/greeting.feature"
    And an issue whose ## Scenarios holds a block with a Feature: line for "specs/features/greeting.feature"
    When the factory runs the issue
    Then the pin holds "specs/features/greeting.feature" with the hash of the block's text after its first line

  Scenario: A scenario with a new name goes at the end of its file
    Given the clone holds "specs/features/greeting.feature" with the scenarios "A name gets a greeting" and "No name gets the world"
    And an issue whose block for "specs/features/greeting.feature" holds a new text of "No name gets the world" and the scenario "A long name is cut"
    When the factory runs the issue
    Then "specs/features/greeting.feature" ends with its text on main, one blank line, and the scenario "A long name is cut"
    And "No name gets the world" is replaced in place

  Scenario: A scenario appended to a file that ends in a blank line follows one blank line
    Given the clone holds "specs/features/greeting.feature", which ends in a blank line
    And an issue whose block for "specs/features/greeting.feature" holds the scenario "A long name is cut"
    When the factory runs the issue
    Then "specs/features/greeting.feature" holds exactly one blank line before "A long name is cut"

  Scenario: A scenario with a name the file holds replaces it in place
    Given the clone holds "specs/features/greeting.feature" with the tagged scenarios "A name gets a greeting" and "No name gets the world", with a blank line and a comment line above the tag of "No name gets the world"
    And an issue whose block for "specs/features/greeting.feature" holds a new text of "A name gets a greeting" with its tag
    When the factory runs the issue
    Then "specs/features/greeting.feature" is its text on main with the span of "A name gets a greeting" replaced by the block's scenario
    And the blank line, the comment, the tag and the text of "No name gets the world" are as they are on main

  Scenario: A replace can turn a scenario into a scenario outline
    Given the clone holds "specs/features/greeting.feature" with the scenarios "A name gets a greeting" and "No name gets the world"
    And an issue whose block for "specs/features/greeting.feature" holds "Scenario Outline: A name gets a greeting" with an Examples table
    When the factory runs the issue
    Then "specs/features/greeting.feature" is its text on main with the span of "A name gets a greeting" replaced by the block's scenario outline

  Scenario: A replaced scenario outline takes its examples with it
    Given the clone holds a scenario outline "Each name gets a greeting" with a tagged Examples table, and a scenario after it
    And an issue whose block holds a new text of "Each name gets a greeting" with a new Examples table
    When the factory runs the issue
    Then the file holds the new Examples table and not the old one
    And the scenario after it is as it is on main

  Scenario: A removed scenario leaves its file
    Given the clone holds "specs/features/greeting.feature" with the tagged scenarios "No name gets the world" and "A name gets a greeting"
    And an issue whose ## Removed scenarios lists "`specs/features/greeting.feature`: No name gets the world"
    When the factory runs the issue
    Then "specs/features/greeting.feature" is its text on main without the span of "No name gets the world" and the blank lines after it
    And "A name gets a greeting" has its own tag alone

  Scenario: A file whose last scenario is removed leaves the work tree and the pin
    Given the clone holds "specs/features/farewell.feature" with one scenario, and "specs/features/greeting.feature"
    And an issue whose ## Removed scenarios lists that one scenario
    When the factory runs the issue
    Then the work tree lacks "specs/features/farewell.feature"
    And the pin lacks "specs/features/farewell.feature"

  Scenario: An issue with Removed scenarios alone asks for the plan alone
    Given an issue with ## Removed scenarios and no ## Scenarios
    When the factory runs the issue
    Then the first plan prompt holds "Write no other file: .factory/plan.md is the only file the plan phase writes."

  Scenario: A body with CRLF line ends gives a feature file with LF line ends
    Given an issue body with CRLF line ends and a block for "specs/features/greeting-name.feature"
    When the factory runs the issue
    Then "specs/features/greeting-name.feature" holds no carriage return

  Scenario: A doc string fenced with backticks under its step stays in the feature file
    Given a block whose step holds a doc string fenced with ``` and indented 6 spaces
    When the factory runs the issue
    Then the feature file holds the doc string and every step after it

  Scenario: The first prompt of an issue with scenarios asks for the plan alone
    Given an issue with ## Scenarios
    When the plan phase runs
    Then the first prompt holds "Write no other file: .factory/plan.md is the only file the plan phase writes."
    And the first prompt names each feature file that the issue wrote
    And the first prompt lacks "Write Gherkin scenarios"

  Scenario: A plan that changes a feature file the issue wrote fails naming it
    Given an issue with ## Scenarios
    When the plan agent changes "specs/features/greeting-name.feature" on the first prompt
    Then the second prompt holds "The file specs/features/greeting-name.feature is changed; write no file outside .factory/ in the plan phase."

  Scenario: A plan that adds a feature file fails naming it
    Given an issue with ## Scenarios
    When the plan agent adds "specs/features/extra.feature" on the first prompt
    Then the second prompt holds "The file specs/features/extra.feature is new; write no file outside .factory/ in the plan phase."

  Scenario Outline: An issue whose scenarios are not sound gets a comment and no run
    Given an issue body with <the fault>
    When the factory runs the issue
    Then the issue gets a comment that holds "<the comment>"
    And the progress lines end with "scenarios: failed"
    And FactoryRun starts no container
    And the run gives back no pull request and 0 Redos

    Examples:
      | the fault | the comment |
      | a ## Scenarios heading with no fenced block | The heading ## Scenarios holds no fenced block. Put the scenarios of each feature file in a fenced block under it. |
      | a block with no closing fence | A fenced block under the heading ## Scenarios has no closing fence. |
      | a block whose first line is "Feature: Greeting" | Each fenced block under the heading ## Scenarios starts with the line # specs/features/<name>.feature. A block starts with 'Feature: Greeting'. |
      | a block whose first line is "# specs/features/../README.feature" | Each fenced block under the heading ## Scenarios starts with the line # specs/features/<name>.feature. A block starts with '# specs/features/../README.feature'. |
      | two blocks for "specs/features/greeting.feature" | Two blocks under the heading ## Scenarios name specs/features/greeting.feature. |
      | a block with no Feature: line for "specs/features/new.feature", which the clone lacks | The file specs/features/new.feature is not on main, so its block holds a Feature: line and the whole file. |
      | a block with no Feature: line that holds text before its first scenario | The block for specs/features/greeting.feature holds text before its first scenario. A block of scenarios starts with a comment, a tag or a scenario. |
      | a block with no Feature: line that holds a Background: or a Rule: | The block for specs/features/greeting.feature holds a Background: or a Rule:. Change them in a block that holds the whole file. |
      | a block of scenarios or a removed scenario for a file on main that holds a Rule: | The file specs/features/greeting.feature holds a Rule:, so change its scenarios in a block that holds the whole file. |
      | a block that holds two scenarios named 'A long name is cut' | The block for specs/features/greeting.feature holds two scenarios named 'A long name is cut'. |
      | a scenario whose name the file holds twice | The feature file specs/features/greeting.feature holds two scenarios named 'A name gets a greeting'. |
      | a block for "specs/features/greeting.feature" that does not parse | The feature file specs/features/greeting.feature does not parse: |
      | a block for "specs/features/greeting.feature" whose doc string text sits deeper than its opening delimiter | The feature file specs/features/greeting.feature: the doc string at line |
      | a block that gives the file the scenarios "A name gets a greeting" and "a name, gets a greeting" | The feature file specs/features/greeting.feature holds the scenarios 'A name gets a greeting' and 'a name, gets a greeting', which give one test name. |
      | a ## Removed scenarios line "specs/features/greeting.feature: No name gets the world" with no backticks | Each line under the heading ## Removed scenarios is a list item: a path in backticks, a colon, and a scenario name. A line reads 'specs/features/greeting.feature: No name gets the world'. |
      | a removed scenario in "specs/features/gone.feature", which the clone lacks | The file specs/features/gone.feature, under ## Removed scenarios, is not on main. |
      | a removed scenario "A lost name" that its file lacks | The feature file specs/features/greeting.feature holds no scenario named 'A lost name'. |
      | a removed scenario whose name a block for the same file also holds | The scenario 'No name gets the world' of specs/features/greeting.feature is under ## Removed scenarios and in a block under ## Scenarios. Keep one of the two. |
      | a removed scenario in a file that a whole-file block replaces | The file specs/features/greeting.feature has a whole-file block, so ## Removed scenarios names none of its scenarios. |
      | removals that leave no feature file | The issue leaves no feature file under specs/features/. |
      | blocks and removals that leave every file as it is on main | The blocks and the removed scenarios leave every feature file as it is on main. |
      | both ## Scenarios and ## Acceptance criteria | The body holds both ## Scenarios and ## Acceptance criteria. Keep one of the two. |
      | a block that starts "# specs/features/greeting.feature" outside ## Scenarios | A fenced block that starts with # specs/features/ sits outside the heading ## Scenarios. |

  Scenario: A comment lists every fault of the body
    Given an issue body with a block with no closing fence and a removed scenario in a file that the clone lacks
    When the factory runs the issue
    Then the issue gets one comment that holds both faults
