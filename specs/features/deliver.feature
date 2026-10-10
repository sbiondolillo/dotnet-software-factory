Feature: The factory delivers a run as a pull request linked from its issue

  Background:
    Given the work tree "/work" is the clone of the run "a1b2c3d4" of the repo "factory-scratch" of the owner "sbiondolillo"
    And the base branch is "main" and the issue 12 has the title "The greeter greets in French"
    And git answers each command with the exit code 0

  @bullet-1
  Scenario: Deliver pushes the run branch to origin
    Given GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run with the token "token"
    Then git pushed the branch "factory/a1b2c3d4" from "/work" to origin

  @bullet-2
  Scenario: Deliver opens a pull request from the run branch into the base branch
    Given GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run with the token "token"
    Then Deliver opened a pull request on "sbiondolillo/factory-scratch" from the branch "factory/a1b2c3d4" into "main" with the title "The greeter greets in French"

  @bullet-3
  Scenario: The body of the pull request starts with the closing line
    Given GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run with the token "token"
    Then the first line of the body of the pull request is "Closes #12"

  @bullet-4
  Scenario: Deliver posts one comment with the pull request link on the issue
    Given GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run with the token "token"
    Then Deliver posted one comment on the issue 12 of "sbiondolillo/factory-scratch" that holds "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-5
  Scenario: Deliver gives back the pull request
    Given GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run with the token "token"
    Then Deliver gives back the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-6
  Scenario: A failed push throws and sends no request to GitHub
    Given git answers the push with the exit code 1
    When a caller delivers the run with the token "token"
    Then Deliver throws GitCommandException
    And Deliver sent no request to GitHub

  @bullet-7
  Scenario: A rejected open of the pull request throws and posts no comment
    Given GitHub answers the open of the pull request with the status code 422
    When a caller delivers the run with the token "token"
    Then Deliver throws GitHubRequestException with the status code 422
    And Deliver posted no comment
