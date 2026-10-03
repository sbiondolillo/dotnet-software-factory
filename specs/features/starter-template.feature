Feature: A new repo starts from the starter template

  @bullet-1
  Scenario: Installing the starter template lists it
    When I install the template from "templates/starter"
    # run: printf '0'
    Then the install exits with the code 0
    # run: printf '%s\n' 'factory-starter' 'dotnet-software-factory starter'
    And the template list shows the short name "factory-starter" with the name "dotnet-software-factory starter"

  @bullet-1
  Scenario: A template that is not installed is not listed
    Given the template is not installed
    # run: printf 'factory-starter'
    Then the template list does not show the short name "factory-starter"

  @bullet-2
  Scenario: The copy holds every starter file and no other
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    # run: printf '0'
    Then the dotnet new command exits with the code 0
    # run: printf '.template.config/'
    And the copy holds every file of the starter outside ".template.config/", each byte-equal to the starter's file
    And the copy holds no other file

  @bullet-2
  Scenario: The copy holds no file of the template configuration
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    # run: printf '.template.config/'
    Then the copy holds no file under ".template.config/"

  @bullet-3
  Scenario: The copy's solution names the four projects
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    Then the copy's Solution.slnx names these projects
      | project                         |
      | src/App/App.csproj              |
      | src/Core/Core.csproj            |
      | tests/Specs/Specs.csproj        |
      | tests/Integration/Integration.csproj |

  @bullet-4
  Scenario: The copy holds one feature file with one scenario and one step class
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    Then the copy's specs/features directory holds one feature file with one scenario
    And the copy's tests/Specs/Steps directory holds one step class

  @bullet-5
  Scenario: The copy's integration suite holds the project file and its lock file only
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    Then the copy's tests/Integration directory holds these files and no other
      | file                  |
      | Integration.csproj    |
      | packages.lock.json    |

  @bullet-6
  Scenario: Each project of the copy has a lock file beside it
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    Then each of the four projects of the copy has a packages.lock.json beside its project file

  @bullet-7
  Scenario: The copy's gitignore holds four lines
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    # run: printf '%s\n' '.factory/' 'bin/' 'obj/' 'TestResults/'
    Then the copy's .gitignore holds the lines ".factory/", "bin/", "obj/" and "TestResults/"

  @bullet-8
  Scenario: The App project references Microsoft.Extensions.Hosting only
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    # run: printf '%s\n' 'src/App/App.csproj' 'Microsoft.Extensions.Hosting'
    Then the copy's "src/App/App.csproj" references the package "Microsoft.Extensions.Hosting" and no other package

  @bullet-9
  Scenario: The command tree is in Core and Program parses with it
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    Then the copy's src/Core directory defines the root command and its argument name
    And the copy's src/App/Program.cs parses its arguments with that root command

  @bullet-10
  Scenario: The Core project references two packages only
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    # run: printf '%s\n' 'src/Core/Core.csproj' 'Microsoft.Extensions.Http.Resilience' 'System.CommandLine'
    Then the copy's "src/Core/Core.csproj" references the packages "Microsoft.Extensions.Http.Resilience" and "System.CommandLine" and no other package

  @bullet-11
  Scenario: The package versions are pinned centrally
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory
    # run: printf '%s\n' 'Microsoft.Extensions.Hosting' '10.0.12' 'System.CommandLine' '2.0.12'
    Then the copy's Directory.Packages.props gives "Microsoft.Extensions.Hosting" the version "10.0.12" and "System.CommandLine" the version "2.0.12"
