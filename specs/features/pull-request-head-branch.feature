Feature: The head branch of a pull request

  @bullet-1
  Scenario: PullRequests returns the head branch of a pull request
    Given GitHub answers the read of pull request 10 of "sbiondolillo/factory-scratch" with the head ref "factory/a1b2c3d4"
    When I read the head branch of pull request 10 of "sbiondolillo/factory-scratch"
    # run: printf 'factory/a1b2c3d4'
    Then the head branch is "factory/a1b2c3d4"

  @bullet-1
  Scenario: PullRequests throws the GitHubRequestException when the read fails
    Given GitHub fails the read of pull request 10 of "sbiondolillo/factory-scratch"
    When I read the head branch of pull request 10 of "sbiondolillo/factory-scratch"
    Then the read throws the GitHubRequestException
