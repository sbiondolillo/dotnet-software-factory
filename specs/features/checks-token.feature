Feature: The console program reads the check runs with FACTORY_CHECKS_TOKEN when the environment holds it

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" set to "github-token-1" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  @bullet-1
  Scenario: run reads the check runs with the checks token
    Given the environment holds "FACTORY_CHECKS_TOKEN" set to "checks-token-2"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer checks-token-2"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"

  @bullet-2
  Scenario: feedback reads the check runs with the checks token
    Given the environment holds "FACTORY_CHECKS_TOKEN" set to "checks-token-2"
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer checks-token-2"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"

  @bullet-3
  Scenario: run without the checks token reads the check runs with the GitHub token
    Given the environment lacks "FACTORY_CHECKS_TOKEN"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer github-token-1"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"
    And the exit code is 0

  @bullet-4
  Scenario: feedback without the checks token reads the check runs with the GitHub token
    Given the environment lacks "FACTORY_CHECKS_TOKEN"
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer github-token-1"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"
    And the exit code is 0

  @bullet-7
  Scenario: The README says the job's own token is passed as FACTORY_CHECKS_TOKEN
    Then the README section "## Start a new repo" holds the line "`factory.yml` and `review-feedback.yml` pass the job's own token to the factory as `FACTORY_CHECKS_TOKEN`, with the permission `checks: read`. The factory reads the check runs of each pull request with it, on a public repo and on a private repo alike. A fine-grained token has no Checks permission, so `FACTORY_GITHUB_TOKEN` reads check runs on a public repo only." after the line that starts "`FACTORY_GITHUB_TOKEN` is a fine-grained token"
