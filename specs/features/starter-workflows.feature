Feature: A repo made from the starter holds the factory's three workflows

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  Scenario: The copy's workflows directory holds the three workflow files
    Then the copy's ".github/workflows/" holds the files "factory.yml", "ci.yml" and "review-feedback.yml"
    And the copy's ".github/workflows/" holds no other file

  Scenario: The CI workflow has two triggers
    Then the workflow "ci.yml" has two triggers, the event "pull_request" and the event "push" on the branch "main"

  Scenario: The CI workflow can only read contents
    Then the workflow "ci.yml" has the permissions "contents: read"

  Scenario: The CI workflow has the jobs build and integration
    Then the workflow "ci.yml" has the jobs "build" and "integration", each on "ubuntu-latest"
    And the job "integration" needs the job "build"

  Scenario: Each CI job checks out and sets up the SDK before its first dotnet command
    Then each job of "ci.yml" checks out the repo and then sets up the .NET SDK that global.json names, before its first dotnet command

  Scenario: The build job runs the four build commands in order
    Then the job "build" of "ci.yml" runs these commands in order
      | command                                       |
      | dotnet restore --locked-mode                  |
      | dotnet build --no-restore                     |
      | dotnet format --verify-no-changes --no-restore |
      | dotnet test --no-restore                      |

  Scenario: The integration job restores and then runs the integration tests
    Then the job "integration" of "ci.yml" runs "dotnet restore --locked-mode" and then "dotnet run --no-restore --project tests/Integration"

  Scenario: Only the integration step of CI has an environment
    Then the step with the integration command in "ci.yml" has "EXAMPLE_API_KEY" in its environment, from the secret of the same name
    And no other step in "ci.yml" has an environment

  Scenario: The README section gives the install and new commands in order
    Then the README section "## Start a new repo" holds the commands "dotnet new install <checkout>/templates/starter" and "dotnet new factory-starter -o <directory>" in that order

  Scenario: The README says to push the copy to main
    Then the README section "## Start a new repo" says, after the two commands, that the person pushes the copy to the branch "main" of a new GitHub repo

  Scenario: The README names the secrets and the token's access
    Then the README section "## Start a new repo" names the Actions secrets "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And it says that "FACTORY_GITHUB_TOKEN" is a fine-grained token that selects the new repo alone, with read and write access to "Contents", "Issues", "Pull requests" and "Workflows"
    And the file "README.md" of this repo holds no line that holds "is private, the token also selects it"
    And the file "README.md" of this repo holds no line that holds "a run cannot push a change under"
    And it says that "factory.yml" and "review-feedback.yml" check the factory out with no token, because the factory is public

  Scenario: The README says main is protected with two required checks
    Then the README section "## Start a new repo" says that the branch "main" is protected, with the checks "build" and "integration" required

  Scenario: The README says Copilot code review is requested
    Then the README section "## Start a new repo" says that Copilot code review is requested on every pull request

  Scenario: The README says how a run starts and which tag the workflow names
    Then the README section "## Start a new repo" says that the repo's owner starts a run with the label "factory" on an issue, and that "factory.yml" names the tag "v1" of the factory

  Scenario: The copy's factory workflow holds the pinned text
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
              default: v1

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
                ref: ${{ inputs.ref || 'v1' }}
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
