Feature: Git

  @bullet-1
  Scenario: The checkout after the clone runs as git in the clone's directory
    Given the clone directory is "/tmp/factory-clone-1"
    And the branch name is "feature-1"
    When Git checks out the new branch in that clone
    # run: printf 'git\n/tmp/factory-clone-1\n'
    Then the CommandRunner receives a CommandSpec whose program is "git"
    And that CommandSpec's working directory is "/tmp/factory-clone-1"

  @bullet-2
  Scenario: The clone's CommandSpec names the repo URL and omits the token
    Given the owner is "sbiondolillo"
    And the repo is "factory-scratch"
    And the token is "faketoken123"
    When Git clones that owner and repo with that token into a new clone directory
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git\nfaketoken123\n'
    Then the clone's CommandSpec's arguments include "https://github.com/sbiondolillo/factory-scratch.git"
    And the clone's CommandSpec's arguments do not include "faketoken123"

  @bullet-3
  Scenario: The checkout's CommandSpec names the subcommand and the branch in order
    Given the clone directory is "/tmp/factory-clone-3"
    And the branch name is "feature-3"
    When Git checks out the new branch in that clone
    # run: printf 'checkout\n-b\nfeature-3\n'
    Then the checkout's CommandSpec's arguments are "checkout", "-b" and "feature-3" in that order

  @bullet-4
  Scenario: The clone's and the push's CommandSpec each carry the token as an environment variable
    Given the clone directory is "/tmp/factory-clone-4"
    And the branch name is "feature-4"
    And the token is "faketoken123"
    When Git clones "sbiondolillo/factory-scratch" with that token into that clone directory
    # run: printf 'FACTORY_GITHUB_TOKEN\nfaketoken123\n'
    Then the clone's CommandSpec holds the environment variable "FACTORY_GITHUB_TOKEN" set to "faketoken123"
    When Git pushes that branch from that clone with that token
    # run: printf 'FACTORY_GITHUB_TOKEN\nfaketoken123\n'
    Then the push's CommandSpec holds the environment variable "FACTORY_GITHUB_TOKEN" set to "faketoken123"

  @bullet-5
  Scenario: The clone's and the push's CommandSpec each clear and then set the credential helper
    Given the clone directory is "/tmp/factory-clone-5"
    And the branch name is "feature-5"
    And the token is "faketoken123"
    When Git clones "sbiondolillo/factory-scratch" with that token into that clone directory
    Then the clone's CommandSpec holds these environment variables:
      | name               | value              |
      | GIT_CONFIG_COUNT   | 2                  |
      | GIT_CONFIG_KEY_0   | credential.helper  |
      | GIT_CONFIG_VALUE_0 |                    |
      | GIT_CONFIG_KEY_1   | credential.helper  |
    When Git pushes that branch from that clone with that token
    Then the push's CommandSpec holds these environment variables:
      | name               | value              |
      | GIT_CONFIG_COUNT   | 2                  |
      | GIT_CONFIG_KEY_0   | credential.helper  |
      | GIT_CONFIG_VALUE_0 |                    |
      | GIT_CONFIG_KEY_1   | credential.helper  |

  @bullet-6
  Scenario: The clone's and the push's credential helper value carries the token placeholder
    Given the clone directory is "/tmp/factory-clone-6"
    And the branch name is "feature-6"
    And the token is "faketoken123"
    When Git clones "sbiondolillo/factory-scratch" with that token into that clone directory
    # run: printf 'GIT_CONFIG_VALUE_1\nusername=\nGIT_CONFIG_VALUE_1\npassword=$FACTORY_GITHUB_TOKEN\n'
    Then the clone's CommandSpec's environment variable "GIT_CONFIG_VALUE_1" holds "username="
    And the clone's CommandSpec's environment variable "GIT_CONFIG_VALUE_1" holds "password=$FACTORY_GITHUB_TOKEN"
    When Git pushes that branch from that clone with that token
    # run: printf 'GIT_CONFIG_VALUE_1\nusername=\nGIT_CONFIG_VALUE_1\npassword=$FACTORY_GITHUB_TOKEN\n'
    Then the push's CommandSpec's environment variable "GIT_CONFIG_VALUE_1" holds "username="
    And the push's CommandSpec's environment variable "GIT_CONFIG_VALUE_1" holds "password=$FACTORY_GITHUB_TOKEN"

  @bullet-7
  Scenario: The commit's CommandSpec carries the committer name and email
    Given the clone directory is "/tmp/factory-clone-7"
    And the commit message is "Update the scratch file"
    And the author name is "Ada Lovelace"
    And the author email is "ada@example.com"
    When Git commits every change in that clone with that message, author name and author email
    # run: printf 'GIT_COMMITTER_NAME\nAda Lovelace\nGIT_COMMITTER_EMAIL\nada@example.com\n'
    Then the commit's CommandSpec holds the environment variable "GIT_COMMITTER_NAME" set to "Ada Lovelace"
    And the commit's CommandSpec holds the environment variable "GIT_COMMITTER_EMAIL" set to "ada@example.com"

  @bullet-8
  Scenario: The head SHA is exactly what git prints
    Given the clone directory is "/tmp/factory-clone-8"
    And the CommandRunner answers the next command with exit code 0 and standard output "903c6f7ea009814ad6cff2d387717be8f34bab03"
    When Git reads the head SHA of that clone
    # run: printf '903c6f7ea009814ad6cff2d387717be8f34bab03\n'
    Then the head SHA is "903c6f7ea009814ad6cff2d387717be8f34bab03"

  @bullet-9
  Scenario: The changed files list holds every file git names
    Given the clone directory is "/tmp/factory-clone-9"
    And the base commit is "main"
    And the CommandRunner answers the next command with exit code 0 and standard output "README.md"
    When Git lists the files changed since that base commit in that clone
    # run: printf 'README.md\n'
    Then the list of changed files holds "README.md"

  @bullet-10
  Scenario: A git command that fails throws a GitCommandException naming the subcommand and the error
    Given the clone directory is "/tmp/factory-clone-10"
    And the branch name is "feature-10"
    And the CommandRunner answers the next command with exit code 1 and standard error "fatal: could not read from remote"
    When Git checks out the new branch in that clone
    # run: printf 'checkout\nfatal: could not read from remote\n'
    Then Git throws a GitCommandException whose message names the subcommand "checkout" and holds the standard error "fatal: could not read from remote"
