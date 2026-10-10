Feature: Git

  Background:
    Given a caller has a Git built over a mock of CommandRunner that records every CommandSpec it runs

  Scenario: Every git operation runs as a git CommandSpec, and every operation after the clone shares its working directory
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-1" into "/tmp/factory-clone-1"
    And Git checks out the new branch "feature-1" in "/tmp/factory-clone-1"
    And Git commits all changes in "/tmp/factory-clone-1" with the message "Update README" and the author name "Ada Lovelace" and the author email "ada@example.com"
    And Git pushes the branch "feature-1" from "/tmp/factory-clone-1" with the token "token-1"
    And Git reads the head SHA in "/tmp/factory-clone-1"
    And Git lists the files changed since "main" in "/tmp/factory-clone-1"
    Then every CommandSpec the mock CommandRunner recorded has the program "git"
    And each of these CommandSpecs has the working directory "/tmp/factory-clone-1":
      | operation           |
      | checkout            |
      | commit              |
      | push                |
      | head read           |
      | changed files read  |

  Scenario: The clone's CommandSpec carries the scratch repo's URL and never the token
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "super-secret-token" into "/tmp/factory-clone-2"
    Then the clone's CommandSpec holds the argument "https://github.com/sbiondolillo/factory-scratch.git"
    And none of the clone's CommandSpec arguments equal "super-secret-token"

  Scenario: The checkout's CommandSpec passes checkout, -b and the branch name in order
    When Git checks out the new branch "release-42" in "/tmp/factory-clone-3"
    Then the checkout's CommandSpec holds the arguments "checkout", "-b" and "release-42" in that order

  Scenario: The clone's and the push's CommandSpecs carry the token as FACTORY_GITHUB_TOKEN
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-4" into "/tmp/factory-clone-4"
    And Git pushes the branch "feature-4" from "/tmp/factory-clone-4" with the token "token-4"
    Then the clone's CommandSpec and the push's CommandSpec each hold the environment variable "FACTORY_GITHUB_TOKEN" set to "token-4"

  Scenario: The clone's and the push's CommandSpecs clear and then set the credential helper through GIT_CONFIG entries
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-5" into "/tmp/factory-clone-5"
    And Git pushes the branch "feature-5" from "/tmp/factory-clone-5" with the token "token-5"
    Then the clone's CommandSpec and the push's CommandSpec each hold these environment variables:
      | name               | value             |
      | GIT_CONFIG_COUNT    | 2                 |
      | GIT_CONFIG_KEY_0    | credential.helper |
      | GIT_CONFIG_VALUE_0  |                   |
      | GIT_CONFIG_KEY_1    | credential.helper |
    And each GIT_CONFIG_VALUE_1 holds "$FACTORY_GITHUB_TOKEN"

  Scenario: The clone's and the push's CommandSpecs' credential helper hands git a username and the token as a password
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-6" into "/tmp/factory-clone-6"
    And Git pushes the branch "feature-6" from "/tmp/factory-clone-6" with the token "token-6"
    Then each GIT_CONFIG_VALUE_1 holds "username=" and "password=$FACTORY_GITHUB_TOKEN"

  Scenario: The commit's CommandSpec carries the given author as the committer
    When Git commits all changes in "/tmp/factory-clone-7" with the message "Update README" and the author name "Ada Lovelace" and the author email "ada@example.com"
    Then the commit's CommandSpec holds the environment variable "GIT_COMMITTER_NAME" set to "Ada Lovelace" and the environment variable "GIT_COMMITTER_EMAIL" set to "ada@example.com"

  Scenario: The head SHA is the 40 characters git prints for the head read
    Given git answers the head read with exit code 0 and the standard output "903c6f7ea009814ad6cff2d387717be8f34bab03"
    When Git reads the head SHA in "/tmp/factory-clone-8"
    Then the head SHA is "903c6f7ea009814ad6cff2d387717be8f34bab03"

  Scenario: The list of changed files holds every file git names since the base commit
    Given git answers the changed-files read with exit code 0 and the standard output "README.md"
    When Git lists the files changed since "main" in "/tmp/factory-clone-9"
    Then the list of changed files holds "README.md"

  Scenario: A git command that fails throws a GitCommandException naming the subcommand and holding its standard error
    Given git answers the checkout with exit code 1 and the standard error "fatal: reference already exists"
    When Git checks out the new branch "feature-10" in "/tmp/factory-clone-10"
    Then Git throws a GitCommandException whose message contains "checkout"
    And the exception message contains "fatal: reference already exists"
