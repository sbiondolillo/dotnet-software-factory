Feature: The starter's review-feedback workflow answers the review threads of a labelled pull request

  Background:
    Given the template is installed
    When I run dotnet new "factory-starter" into an empty directory

  Scenario: The copy's review-feedback workflow holds the pinned text
    Then the copy's workflow "review-feedback.yml" holds this text
      """
      # Answers the review threads of a pull request when the repo's owner labels it `review-feedback`.
      name: review-feedback

      on:
        pull_request_target:
          types: [labeled]

      # The job's own token reads the check runs of the pull request. A fine-grained token has no Checks permission on a private repo.
      permissions:
        checks: read

      concurrency:
        group: review-feedback-${{ github.event.pull_request.number }}

      jobs:
        feedback:
          runs-on: ubuntu-latest
          if: github.event.label.name == 'review-feedback' && github.event.sender.login == github.repository_owner
          steps:
            - name: Remove the label
              env:
                GH_TOKEN: ${{ secrets.FACTORY_GITHUB_TOKEN }}
              run: gh api -X DELETE repos/${{ github.repository }}/issues/${{ github.event.pull_request.number }}/labels/review-feedback
            - uses: actions/checkout@v7
              with:
                repository: sbiondolillo/dotnet-software-factory
                ref: v1
            - uses: actions/setup-dotnet@v6
              with:
                global-json-file: global.json
            - name: Answer the review threads
              env:
                FACTORY_GITHUB_TOKEN: ${{ secrets.FACTORY_GITHUB_TOKEN }}
                FACTORY_CHECKS_TOKEN: ${{ github.token }}
                ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
                EXAMPLE_API_KEY: ${{ secrets.EXAMPLE_API_KEY }}
              run: dotnet run --project src/App -c Release -- feedback --repo ${{ github.repository }} --pr ${{ github.event.pull_request.number }} --pass-env EXAMPLE_API_KEY
      """

  Scenario: The copy's workflows directory holds exactly the three workflow files
    Then the copy's ".github/workflows/" holds the files "factory.yml", "ci.yml" and "review-feedback.yml"
    And the copy's ".github/workflows/" holds no other file
