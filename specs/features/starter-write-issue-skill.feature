Feature: A repo made from the starter holds the skill write-issue

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  Scenario: The skill directory holds SKILL.md, REVIEW.md and PROOF.md and no other file
    Then the copy's directory ".claude/skills/write-issue" holds the files "SKILL.md", "REVIEW.md" and "PROOF.md" and no other file

  Scenario: The example body of the write-an-issue page applies to the copy
    Given the body in the markdown block of the copy's "docs/write-an-issue.md"
    And the copy's "specs/features/example.feature" holds this text
      """
      Feature: Example

        Scenario: The program runs
          When the program runs
          Then the program exits with the code 0
      """
    When the scenarios of that body are applied to the copy
    Then the apply gives no fault
    And the copy holds "specs/features/new-example.feature"
    And the copy's "specs/features/example.feature" ends with the scenario "An empty input gets the default answer"
    And the copy's "specs/features/example.feature" still holds the scenario "The program runs"

  Scenario: The awk program of PROOF.md saves the check of a body in the form of the template
    Given the draft body
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Scope

      - `tests/Specs/Steps/GreetingSteps.cs`

      ## Check

      ```sh
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves the check of a body with CRLF line ends
    Given the draft body, with CRLF line ends
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Scope

      - `tests/Specs/Steps/GreetingSteps.cs`

      ## Check

      ```sh
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet restore --locked-mode &&
      grep -qF 'Assert.Equal(expected, _greeting)' tests/Specs/Steps/GreetingSteps.cs
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves a check from an indented tilde fence
    Given the draft body
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Check

        ~~~sh
      dotnet restore --locked-mode &&
      dotnet build --no-restore
        ~~~
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet restore --locked-mode &&
      dotnet build --no-restore
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves a check whose fence holds a shorter fence and a heading line
    Given the draft body
      """
      Reframe. Raise IDE0022.

      ## Check

      ````sh
      ## 1. No obj and no bin
      test -z "$(find . \( -name obj -o -name bin \) -print)" &&
      cat > notes.md <<'EOF' &&
      ```
      EOF
      printf '%s\n' '```' | grep -qx '```'
      ````
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      ## 1. No obj and no bin
      test -z "$(find . \( -name obj -o -name bin \) -print)" &&
      cat > notes.md <<'EOF' &&
      ```
      EOF
      printf '%s\n' '```' | grep -qx '```'
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves the check under the heading, past a fenced example that holds the heading
    Given the draft body
      """
      Cleanup. The page shows the form of a check:

      ~~~markdown
      ## Check

      ```sh
      false
      ```
      ~~~

      ## Check

      ```sh
      true
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      true
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check from a body whose fence is not closed
    Given the draft body
      """
      Refactor. Move the class Greeter to src/Core/Greeting.

      ## Check

      ```sh
      test -f src/Core/Greeting/Greeter.cs
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves a check past a trailing space, a tab, a longer closing fence and a backtick info string
    Given the draft body, with each "<sp>" as a space and each "<tab>" as a tab
      """
      Cleanup. The step that reads the greeting compares it whole.

      ## Check<sp>

      ```sh `not a fence`
      <tab>~~~sh
      dotnet build --no-restore
      <tab>~~~~<sp>
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      dotnet build --no-restore
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check from a blank block
    Given the draft body
      """
      ## Check

      ```sh

      ```

      ```sh
      true
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check when the first Check heading holds no block
    Given the draft body
      """
      ## Check

      No block here.

      ## Check

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves the first block under the heading and no later block
    Given the draft body
      """
      ## Check

      ```sh
      true
      ```

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      true
      """
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check from a block under a subheading
    Given the draft body
      """
      ## Check

      ### Note

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md saves no check under a heading other than ## Check
    Given the draft body
      """
      ## Checklist

      ```sh
      false
      ```

      ### Check

      ```sh
      false
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is empty
    And CleanupBody reads the same check from that body

  Scenario: The awk program of PROOF.md reads a line that starts with a hash and is no heading as text
    Given the draft body
      """
      ## Check

      #242 holds the reason for this check.

      ####### x

      ```sh
      true
      ```
      """
    When the awk program of the copy's ".claude/skills/write-issue/PROOF.md" saves the check of that body
    Then the saved check is this text
      """
      true
      """
    And CleanupBody reads the same check from that body
