Feature: The console program reads the check runs with FACTORY_CHECKS_TOKEN when the environment holds it

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" set to "github-token-1" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: run reads the check runs with the checks token
    Given the environment holds "FACTORY_CHECKS_TOKEN" set to "checks-token-2"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer checks-token-2"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"

  Scenario: feedback reads the check runs with the checks token
    Given the environment holds "FACTORY_CHECKS_TOKEN" set to "checks-token-2"
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer checks-token-2"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"

  Scenario: run without the checks token reads the check runs with the GitHub token
    Given the environment lacks "FACTORY_CHECKS_TOKEN"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer github-token-1"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"
    And the exit code is 0

  Scenario: feedback without the checks token reads the check runs with the GitHub token
    Given the environment lacks "FACTORY_CHECKS_TOKEN"
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then every read of the check runs holds the header "Authorization" with the value "Bearer github-token-1"
    And every other request to GitHub's API holds the header "Authorization" with the value "Bearer github-token-1"
    And the exit code is 0
