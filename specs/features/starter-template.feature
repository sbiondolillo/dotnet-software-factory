Feature: Starter template

  @bullet-1
  Scenario: Installing the starter template lists it
    Given a fresh dotnet home
    When I run dotnet new install on "templates/starter"
    # run: printf '0\nfactory-starter\ndotnet-software-factory starter'
    Then the command exits with the code 0
    And dotnet new list lists the short name "factory-starter" with the name "dotnet-software-factory starter"

  @bullet-1
  Scenario: Installing the starter template twice is refused
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new install on "templates/starter"
    # run: printf '106'
    Then the command exits with the code 106

  @bullet-2
  Scenario: A new repo holds every starter file byte for byte
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf '0\n.template.config/'
    Then the command exits with the code 0
    And the directory holds every file of the starter outside ".template.config/", each byte-equal to the starter's file, and no other file

  @bullet-2
  Scenario: A new repo cannot be made without the installed template
    Given a fresh dotnet home
    When I run dotnet new factory-starter into an empty directory
    # run: printf '103'
    Then the command exits with the code 103

  @bullet-3
  Scenario: The solution names the four projects
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    Then the copy's Solution.slnx names the four projects of the copy

  @bullet-4
  Scenario: The copy holds one feature file with one scenario and one step class
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    Then the copy's specs/features directory holds one feature file with one scenario
    And the copy's tests/Specs/Steps directory holds one step class

  @bullet-5
  Scenario: The integration suite holds a project file and a lock file only
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf 'Integration.csproj\npackages.lock.json'
    Then the copy's tests/Integration directory holds "Integration.csproj" and "packages.lock.json", and those two files only

  @bullet-6
  Scenario: Each project has a lock file beside its project file
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    Then each of the four projects of the copy has a packages.lock.json beside its project file

  @bullet-7
  Scenario: The gitignore holds the four lines
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf '.factory/\nbin/\nobj/\nTestResults/'
    Then the copy's .gitignore holds the lines ".factory/", "bin/", "obj/" and "TestResults/"

  @bullet-8
  Scenario: The console project references only the hosting package
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf 'Microsoft.Extensions.Hosting'
    Then the copy's src/App/App.csproj references the package "Microsoft.Extensions.Hosting", and no other package

  @bullet-9
  Scenario: The library defines the root command and the program parses with it
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf 'name'
    Then the copy's src/Core defines the root command and its argument "name"
    And the copy's src/App/Program.cs parses its arguments with that root command

  @bullet-10
  Scenario: The library project references its two packages only
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf 'Microsoft.Extensions.Http.Resilience\nSystem.CommandLine'
    Then the copy's src/Core/Core.csproj references the packages "Microsoft.Extensions.Http.Resilience" and "System.CommandLine", and no other package

  @bullet-11
  Scenario: The package versions are pinned centrally
    Given a fresh dotnet home
    And the starter template is installed
    When I run dotnet new factory-starter into an empty directory
    # run: printf 'Microsoft.Extensions.Hosting\n10.0.12\nSystem.CommandLine\n2.0.12'
    Then the copy's Directory.Packages.props gives "Microsoft.Extensions.Hosting" the version "10.0.12" and "System.CommandLine" the version "2.0.12"
