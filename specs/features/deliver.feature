Feature: The factory delivers a run as a pull request linked from its issue

  Background:
    Given the work tree "/work", the run id "a1b2c3d4", the repo "factory-scratch" of the owner "sbiondolillo", the base branch "main" and the token "token"
    And the issue is 12 with the title "The greeter greets in French"

  @bullet-1
  Scenario: Delivering pushes the run branch from the work tree to origin
    Given git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run
    # run: printf '%s\n' 'factory/a1b2c3d4' '/work'
    Then git pushes the branch "factory/a1b2c3d4" from "/work" to origin

  @bullet-2
  Scenario: Delivering opens a pull request from the run branch into the base branch with the issue title
    Given git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run
    # run: printf '%s\n' 'sbiondolillo/factory-scratch' 'factory/a1b2c3d4' 'main' 'The greeter greets in French'
    Then Deliver opens a pull request on "sbiondolillo/factory-scratch" from the branch "factory/a1b2c3d4" into "main" with the title "The greeter greets in French"

  @bullet-3
  Scenario: The pull request body starts with the closing line
    Given git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run
    # run: printf '%s' 'Closes #12'
    Then the body of the pull request starts with the line "Closes #12"

  @bullet-4
  Scenario: Delivering posts one comment with the pull request address on the issue
    Given git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run
    # run: printf '%s\n' 12 'sbiondolillo/factory-scratch'
    Then Deliver posts one comment on the issue 12 of "sbiondolillo/factory-scratch"
    # run: printf '%s' 'https://github.com/sbiondolillo/factory-scratch/pull/10'
    And the comment holds "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-5
  Scenario: Delivering gives back the pull request
    Given git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller delivers the run
    # run: printf '%s\n' 10 'https://github.com/sbiondolillo/factory-scratch/pull/10'
    Then Deliver gives back the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-6
  Scenario: A failed push throws and sends no request to GitHub
    Given git answers the push with the exit code 1
    When a caller delivers the run
    Then Deliver throws GitCommandException
    And no request was sent to GitHub

  @bullet-7
  Scenario: A rejected open of the pull request throws and posts no comment
    Given git answers each command with the exit code 0
    And GitHub answers the open of the pull request with the status code 422
    When a caller delivers the run
    # run: printf '%s' 422
    Then Deliver throws GitHubRequestException with the status code 422
    And no comment was posted
