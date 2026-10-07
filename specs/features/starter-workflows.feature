Feature: A repo made from the starter holds the factory's three workflows

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  @bullet-1
  Scenario: The copy's workflows directory holds the three workflow files
    # run: printf '%s\n' '.github/workflows/' 'factory.yml' 'ci.yml' 'review-feedback.yml' '.github/workflows/'
    Then the copy's ".github/workflows/" holds the files "factory.yml", "ci.yml" and "review-feedback.yml"
    And the copy's ".github/workflows/" holds no other file

  @bullet-9
  Scenario: The CI workflow has two triggers
    # run: printf '%s\n' 'ci.yml' 'pull_request' 'push' 'main'
    Then the workflow "ci.yml" has two triggers, the event "pull_request" and the event "push" on the branch "main"

  @bullet-10
  Scenario: The CI workflow can only read contents
    # run: printf '%s\n' 'ci.yml' 'contents: read'
    Then the workflow "ci.yml" has the permissions "contents: read"

  @bullet-11
  Scenario: The CI workflow has the jobs build and integration
    # run: printf '%s\n' 'ci.yml' 'build' 'integration' 'ubuntu-latest' 'integration' 'build'
    Then the workflow "ci.yml" has the jobs "build" and "integration", each on "ubuntu-latest"
    And the job "integration" needs the job "build"

  @bullet-12
  Scenario: Each CI job checks out and sets up the SDK before its first dotnet command
    # run: printf '%s\n' 'ci.yml'
    Then each job of "ci.yml" checks out the repo and then sets up the .NET SDK that global.json names, before its first dotnet command

  @bullet-13
  Scenario: The build job runs the four build commands in order
    # run: printf '%s\n' 'build' 'ci.yml'
    Then the job "build" of "ci.yml" runs these commands in order
      | command                                       |
      | dotnet restore --locked-mode                  |
      | dotnet build --no-restore                     |
      | dotnet format --verify-no-changes --no-restore |
      | dotnet test --no-restore                      |

  @bullet-14
  Scenario: The integration job restores and then runs the integration tests
    # run: printf '%s\n' 'integration' 'ci.yml' 'dotnet restore --locked-mode' 'dotnet run --no-restore --project tests/Integration'
    Then the job "integration" of "ci.yml" runs "dotnet restore --locked-mode" and then "dotnet run --no-restore --project tests/Integration"

  @bullet-15
  Scenario: Only the integration step of CI has an environment
    # run: printf '%s\n' 'ci.yml' 'EXAMPLE_API_KEY' 'ci.yml'
    Then the step with the integration command in "ci.yml" has "EXAMPLE_API_KEY" in its environment, from the secret of the same name
    And no other step in "ci.yml" has an environment

  @bullet-16
  Scenario: The README section gives the install and new commands in order
    # run: printf '%s\n' '## Start a new repo' 'dotnet new install <checkout>/templates/starter' 'dotnet new factory-starter -o <directory>'
    Then the README section "## Start a new repo" holds the commands "dotnet new install <checkout>/templates/starter" and "dotnet new factory-starter -o <directory>" in that order

  @bullet-17
  Scenario: The README says to push the copy to main
    # run: printf '%s\n' '## Start a new repo' 'main'
    Then the README section "## Start a new repo" says, after the two commands, that the person pushes the copy to the branch "main" of a new GitHub repo

  @bullet-18
  Scenario: The README names the secrets and the token's access
    # run: printf '%s\n' '## Start a new repo' 'FACTORY_GITHUB_TOKEN' 'ANTHROPIC_API_KEY' 'FACTORY_GITHUB_TOKEN' 'Contents' 'Issues' 'Pull requests' 'Contents' 'sbiondolillo/dotnet-software-factory'
    Then the README section "## Start a new repo" names the Actions secrets "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And it says that "FACTORY_GITHUB_TOKEN" is a fine-grained token with read and write access to "Contents", "Issues" and "Pull requests" on the new repo, and read access to "Contents" on "sbiondolillo/dotnet-software-factory"

  @bullet-20
  Scenario: The README says main is protected with two required checks
    # run: printf '%s\n' '## Start a new repo' 'main' 'build' 'integration'
    Then the README section "## Start a new repo" says that the branch "main" is protected, with the checks "build" and "integration" required

  @bullet-21
  Scenario: The README says Copilot code review is requested
    # run: printf '%s\n' '## Start a new repo'
    Then the README section "## Start a new repo" says that Copilot code review is requested on every pull request

  @bullet-22
  Scenario: The README says how a run starts and which tag the workflow names
    # run: printf '%s\n' '## Start a new repo' 'factory' 'factory.yml' 'v0'
    Then the README section "## Start a new repo" says that the repo's owner starts a run with the label "factory" on an issue, and that "factory.yml" names the tag "v0" of the factory

  @bullet-5
  Scenario: The copy's factory workflow holds the pinned text
    # run: printf '%s\n' 'factory.yml'
    Then the copy's workflow "factory.yml" holds this text
      """
      # Starts a factory run when the repo's owner labels an issue `factory`, or by hand at a chosen ref.
      name: factory

      on:
        issues:
          types: [labeled]
        workflow_dispatch:
          inputs:
            issue:
              description: The number of the issue to run
              required: true
              type: number
            ref:
              description: The branch, tag or commit of the factory
              default: v0

      # The job's own token reads the check runs of the pull request. A fine-grained token has no Checks permission on a private repo.
      permissions:
        checks: read

      jobs:
        factory:
          runs-on: ubuntu-latest
          if: github.event_name == 'workflow_dispatch' || (github.event.label.name == 'factory' && github.event.sender.login == github.repository_owner)
          steps:
            - uses: actions/checkout@v7
              with:
                repository: sbiondolillo/dotnet-software-factory
                ref: ${{ inputs.ref || 'v0' }}
                token: ${{ secrets.FACTORY_GITHUB_TOKEN }}
            - uses: actions/setup-dotnet@v6
              with:
                global-json-file: global.json
            - name: Run the factory
              env:
                FACTORY_GITHUB_TOKEN: ${{ secrets.FACTORY_GITHUB_TOKEN }}
                FACTORY_CHECKS_TOKEN: ${{ github.token }}
                ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
                EXAMPLE_API_KEY: ${{ secrets.EXAMPLE_API_KEY }}
              run: dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --pass-env EXAMPLE_API_KEY
      """
