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
              description: The branch, tag or full 40-character commit SHA of the factory
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
            - name: Push the transcripts to the store
              # The store is a private repo. With no store, the step exits 0 at once. The log prints no transcript.
              if: always()
              env:
                STORE_REPO: ${{ secrets.STORE_REPO }}
                STORE_DEPLOY_KEY: ${{ secrets.STORE_DEPLOY_KEY }}
              run: |
                if [ -z "$STORE_REPO" ] || [ -z "$STORE_DEPLOY_KEY" ]; then exit 0; fi
                key="$RUNNER_TEMP/store-key"
                printf '%s\n' "$STORE_DEPLOY_KEY" > "$key"
                chmod 600 "$key"
                export GIT_SSH_COMMAND="ssh -i $key -o StrictHostKeyChecking=accept-new"
                export GIT_AUTHOR_NAME=factory GIT_AUTHOR_EMAIL=factory@users.noreply.github.com GIT_COMMITTER_NAME=factory GIT_COMMITTER_EMAIL=factory@users.noreply.github.com
                store="$RUNNER_TEMP/store"
                git clone -q "git@github.com:$STORE_REPO.git" "$store"
                mkdir -p "$store/runs/$GITHUB_RUN_ID"
                if [ -d .factory/sandbox-home ]; then
                  find .factory/sandbox-home -type f -name '*.jsonl' -exec cp --parents -t "$store/runs/$GITHUB_RUN_ID" {} +
                fi
                cd "$store"
                git add runs
                if git diff --cached --quiet; then exit 0; fi
                git commit -q -m "Run $GITHUB_RUN_ID of $GITHUB_REPOSITORY"
                for wait in 1 2 3 4 5; do
                  if git push -q 2>/dev/null; then exit 0; fi
                  sleep "$wait" && git pull -q --rebase
                done
                git push -q
      """
