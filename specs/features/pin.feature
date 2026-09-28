Feature: Pin

  @bullet-1
  Scenario: A pin holds the settings and feature files with their hashes
    Given a directory holding these files
      | path                            | content |
      | global.json                     | {}      |
      | specs/features/greeting.feature | Feature |
      | src/Core/Greeter.cs             | class   |
    When I take a pin of the directory
    # run: printf 'global.json\n'; printf '{}' | sha256sum | cut -d' ' -f1
    Then the pin holds "global.json" with hash "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"
    # run: printf 'specs/features/greeting.feature\n'; printf 'Feature' | sha256sum | cut -d' ' -f1
    And the pin holds "specs/features/greeting.feature" with hash "3d377ae910dce03ac324d1f4391c0d4d175825e630baf9c5b93d414d7106cfd8"
    # run: printf 'src/Core/Greeter.cs'
    And the pin holds no "src/Core/Greeter.cs"

  @bullet-2
  Scenario: An unchanged directory compares clean
    Given a directory holding these files
      | path                            | content |
      | .editorconfig                   | root    |
      | specs/features/greeting.feature | Feature |
    And I have taken a pin of the directory
    When I compare the pin with the directory
    Then the changed, added and removed lists are all empty

  @bullet-3
  Scenario: Changed bytes in pinned files are reported as changed
    Given a directory holding these files
      | path                            | content |
      | .editorconfig                   | root    |
      | specs/features/greeting.feature | Feature |
    And I have taken a pin of the directory
    And one byte of ".editorconfig" changes
    And one byte of "specs/features/greeting.feature" changes
    When I compare the pin with the directory
    # run: printf '.editorconfig\nspecs/features/greeting.feature\n'
    Then the changed list holds ".editorconfig" and "specs/features/greeting.feature"
    And the added and removed lists are empty

  @bullet-4
  Scenario: A pinned file missing from the directory is removed
    Given a directory holding these files
      | path         | content |
      | nuget.config | <a/>    |
    And I have taken a pin of the directory
    And the directory lacks "nuget.config"
    When I compare the pin with the directory
    # run: printf 'nuget.config'
    Then the removed list holds "nuget.config"
    And the changed and added lists are empty

  @bullet-5
  Scenario: A new feature file at depth is added
    Given a directory holding these files
      | path                            | content |
      | specs/features/greeting.feature | Feature |
    And I have taken a pin of the directory
    And the directory gains "specs/features/billing/notes.md" with content "notes"
    When I compare the pin with the directory
    # run: printf 'specs/features/billing/notes.md'
    Then the added list holds "specs/features/billing/notes.md"
    And the changed and removed lists are empty

  @bullet-6
  Scenario: A new settings file in a subdirectory is added
    Given a directory holding these files
      | path        | content |
      | global.json | {}      |
    And I have taken a pin of the directory
    And the directory gains "src/Core/Directory.Build.props" with content "<Project/>"
    When I compare the pin with the directory
    # run: printf 'src/Core/Directory.Build.props'
    Then the added list holds "src/Core/Directory.Build.props"
    And the changed and removed lists are empty

  @bullet-7
  Scenario: A change to an unpinned file is ignored
    Given a directory holding these files
      | path                | content |
      | global.json         | {}      |
      | src/Core/Greeter.cs | class   |
    And I have taken a pin of the directory
    And one byte of "src/Core/Greeter.cs" changes
    When I compare the pin with the directory
    Then the changed, added and removed lists are all empty

  @bullet-8
  Scenario: A settings file under a bin directory is skipped
    Given a directory holding these files
      | path        | content |
      | global.json | {}      |
    And I have taken a pin of the directory
    And the directory gains "tests/Specs/bin/Debug/net10.0/reqnroll.json" with content "{}"
    When I compare the pin with the directory
    Then the changed, added and removed lists are all empty
