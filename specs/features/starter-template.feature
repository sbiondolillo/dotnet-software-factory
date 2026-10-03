Feature: The starter template makes a new repo that builds with no edit

  @bullet-1
  Scenario: Installing the starter template lists it
    Given a fresh dotnet home with no template installed
    When I install the template from "templates/starter"
    # run: printf '%s\n' 0
    Then the install exits with the code 0
    # run: printf '%s\n' 'factory-starter' 'dotnet-software-factory starter'
    And the template list shows the short name "factory-starter" with the name "dotnet-software-factory starter"

  @bullet-1
  Scenario: Installing the same folder twice is refused
    Given a fresh dotnet home with no template installed
    And I install the template from "templates/starter"
    When I install the template from "templates/starter"
    # run: printf '%s\n' 106
    Then the install exits with the code 106

  @bullet-2
  Scenario: The copy holds every starter file outside the template config
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' 0
    Then the creation exits with the code 0
    # run: printf '%s\n' '.template.config/'
    And the copy holds every file of the starter outside ".template.config/", each byte-equal to the starter's file, and no other file

  @bullet-2
  Scenario: Creating a copy with no template installed fails
    Given a fresh dotnet home with no template installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' 103
    Then the creation exits with the code 103
    And the copy is not written

  @bullet-3
  Scenario: The copy's solution names the four projects
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    Then the copy's Solution.slnx names these projects
      | project                          |
      | src/App/App.csproj               |
      | src/Core/Core.csproj             |
      | tests/Specs/Specs.csproj         |
      | tests/Integration/Integration.csproj |

  @bullet-4
  Scenario: The copy holds one feature file and one step class
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    Then the copy's specs/features directory holds one feature file with one scenario
    And the copy's tests/Specs/Steps directory holds one step class

  @bullet-5
  Scenario: The copy's integration suite holds only its project and lock file
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    Then the copy's tests/Integration directory holds Integration.csproj and packages.lock.json, and those two files only

  @bullet-6
  Scenario: Each project of the copy has a lock file beside it
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    Then each of the four projects of the copy has a packages.lock.json beside its project file

  @bullet-7
  Scenario: The copy's gitignore holds the four lines
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' '.factory/' 'bin/' 'obj/' 'TestResults/'
    Then the copy's .gitignore holds the lines ".factory/", "bin/", "obj/" and "TestResults/"

  @bullet-8
  Scenario: The console program references only the hosting package
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' 'Microsoft.Extensions.Hosting'
    Then the copy's src/App/App.csproj references the package "Microsoft.Extensions.Hosting" and no other package

  @bullet-9
  Scenario: The library defines the root command and the program parses with it
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' 'name'
    Then the copy's src/Core defines the root command and its argument "name"
    And the copy's src/App/Program.cs parses its arguments with that root command

  @bullet-10
  Scenario: The library references only its two packages
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' 'Microsoft.Extensions.Http.Resilience' 'System.CommandLine'
    Then the copy's src/Core/Core.csproj references the packages "Microsoft.Extensions.Http.Resilience" and "System.CommandLine", and no other package

  @bullet-11
  Scenario: The copy's package versions are pinned
    Given the starter template is installed
    When I create a copy named "factory-starter" in an empty directory
    # run: printf '%s\n' 'Microsoft.Extensions.Hosting' 10.0.12 'System.CommandLine' 2.0.12
    Then the copy's Directory.Packages.props gives "Microsoft.Extensions.Hosting" the version "10.0.12" and "System.CommandLine" the version "2.0.12"
