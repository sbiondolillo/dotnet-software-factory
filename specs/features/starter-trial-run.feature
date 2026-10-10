Feature: A repo made from the starter starts a trial run of the factory at a chosen ref

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  @bullet-1
  Scenario: The factory workflow has the triggers labeled issue and manual start
    Then the workflow "factory.yml" has the trigger, the event "issues" with the type "labeled"
    And the workflow "factory.yml" has the trigger, the event "workflow_dispatch"
    And the workflow "factory.yml" has no other trigger

  @bullet-2
  Scenario: The manual start has the inputs issue and ref
    Then the trigger of "factory.yml" "workflow_dispatch" has the required input "issue" with the type "number"
    And it has the input "ref" with the default "v0"

  @bullet-3
  Scenario: The factory job runs on ubuntu-latest with the manual or labeled-by-owner condition
    Then the workflow "factory.yml" has one job
    And that job runs on "ubuntu-latest" with the condition "github.event_name == 'workflow_dispatch' || (github.event.label.name == 'factory' && github.event.sender.login == github.repository_owner)"

  @bullet-4
  Scenario: The factory job first checks out the factory at the chosen ref
    Then the first step of the job in "factory.yml" checks out the repo "sbiondolillo/dotnet-software-factory" at the ref "${{ inputs.ref || 'v0' }}" with the token "${{ secrets.FACTORY_GITHUB_TOKEN }}"

  @bullet-5
  Scenario: The factory job sets up the SDK before the last step
    Then a step of the job in "factory.yml" sets up the .NET SDK that the global.json of the checkout names, before the step with the run command "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --pass-env EXAMPLE_API_KEY"

  @bullet-6
  Scenario: The factory job's last step runs the run command with the chosen issue
    Then the last step of the job in "factory.yml" runs "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --pass-env EXAMPLE_API_KEY"

  @bullet-7
  Scenario: The README says the owner starts a trial run at another ref
    Then the README section "## Start a new repo" says that the repo's owner starts a trial run of the factory at another ref with the command "gh workflow run factory -f issue=<number> -f ref=<ref>"
