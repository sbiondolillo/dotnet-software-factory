Feature: Pin fingerprints the feature files and build settings

  @bullet-1
  Scenario: A pin holds the pinned files with their hashes and no source file
    Given a directory holds the file "global.json" with the content "{}"
    And the directory holds the file "specs/features/greeting.feature" with the content "Feature: Greeting"
    And the directory holds the file "src/Core/Greeter.cs" with the content "class Greeter {}"
    When I take a pin of the directory
    # run: printf '%s\n' 'global.json' '44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a'
    Then the pin holds "global.json" with the hash "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"
    # run: printf '%s\n' 'specs/features/greeting.feature' 'f828a703f5041c016eab1522aa19921463ecbe5ed171d1170be81f16fa6adfed'
    And the pin holds "specs/features/greeting.feature" with the hash "f828a703f5041c016eab1522aa19921463ecbe5ed171d1170be81f16fa6adfed"
    # run: printf '%s\n' 'src/Core/Greeter.cs'
    And the pin holds no "src/Core/Greeter.cs"

  @bullet-2
  Scenario: A directory compared with its own pin has no differences
    Given a directory holds the file "global.json" with the content "{}"
    And the directory holds the file "specs/features/greeting.feature" with the content "Feature: Greeting"
    And I have taken a pin of the directory
    When I compare the directory with the pin
    Then the changed, added and removed lists are all empty

  @bullet-3
  Scenario: A changed byte in a pinned file is reported as changed
    Given a directory holds the file ".editorconfig" with the content "root = true"
    And the directory holds the file "specs/features/greeting.feature" with the content "Feature: Greeting"
    And I have taken a pin of the directory
    When one byte of ".editorconfig" changes
    And one byte of "specs/features/greeting.feature" changes
    And I compare the directory with the pin
    # run: printf '%s\n' '.editorconfig' 'specs/features/greeting.feature'
    Then the changed list holds ".editorconfig" and "specs/features/greeting.feature"
    And the added list is empty
    And the removed list is empty

  @bullet-4
  Scenario: A pinned file missing from the directory is reported as removed
    Given a directory holds the file "nuget.config" with the content "<configuration/>"
    And I have taken a pin of the directory
    And the file "nuget.config" is deleted from the directory
    When I compare the directory with the pin
    # run: printf '%s\n' 'nuget.config'
    Then the removed list holds "nuget.config"
    And the changed list is empty
    And the added list is empty

  @bullet-5
  Scenario: A new file at depth under specs/features is reported as added
    Given a directory holds the file "global.json" with the content "{}"
    And I have taken a pin of the directory
    And the directory holds the file "specs/features/billing/notes.md" with the content "notes"
    When I compare the directory with the pin
    # run: printf '%s\n' 'specs/features/billing/notes.md'
    Then the added list holds "specs/features/billing/notes.md"
    And the changed list is empty
    And the removed list is empty

  @bullet-6
  Scenario: A new Directory.Build.props in a subdirectory is reported as added
    Given a directory holds the file "global.json" with the content "{}"
    And I have taken a pin of the directory
    And the directory holds the file "src/Core/Directory.Build.props" with the content "<Project/>"
    When I compare the directory with the pin
    # run: printf '%s\n' 'src/Core/Directory.Build.props'
    Then the added list holds "src/Core/Directory.Build.props"
    And the changed list is empty
    And the removed list is empty

  @bullet-7
  Scenario: Four new build settings files at several depths are all reported as added
    Given a directory holds the file "global.json" with the content "{}"
    And I have taken a pin of the directory
    And the directory holds the file "NuGet.Config" with the content "<configuration/>"
    And the directory holds the file "src/NuGet.config" with the content "<configuration/>"
    And the directory holds the file "src/Core/.globalconfig" with the content "is_global = true"
    And the directory holds the file "Directory.Build.rsp" with the content "-v:m"
    When I compare the directory with the pin
    # run: printf '%s\n' 'NuGet.Config' 'src/NuGet.config' 'src/Core/.globalconfig' 'Directory.Build.rsp'
    Then the added list holds "NuGet.Config", "src/NuGet.config", "src/Core/.globalconfig" and "Directory.Build.rsp"
    And the changed list is empty
    And the removed list is empty

  @bullet-8
  Scenario: A changed source file is not reported
    Given a directory holds the file "global.json" with the content "{}"
    And the directory holds the file "src/Core/Greeter.cs" with the content "class Greeter {}"
    And I have taken a pin of the directory
    When one byte of "src/Core/Greeter.cs" changes
    And I compare the directory with the pin
    Then the changed, added and removed lists are all empty

  @bullet-9
  Scenario: A reqnroll.json under a bin directory is skipped
    Given a directory holds the file "global.json" with the content "{}"
    And I have taken a pin of the directory
    And the directory holds the file "tests/Specs/bin/Debug/net10.0/reqnroll.json" with the content "{}"
    When I compare the directory with the pin
    Then the changed, added and removed lists are all empty
