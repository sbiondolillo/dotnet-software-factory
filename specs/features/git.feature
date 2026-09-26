Feature: Git clones, branches, commits and pushes with git

  @bullet-1
  Scenario: Every git operation runs as a git CommandSpec, and every operation after the clone uses the clone's directory
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-1"
    And I check out the branch "factory/run" in "/tmp/factory-clone-1"
    And I commit all changes in "/tmp/factory-clone-1" with the message "Do the work", the author name "Factory Bot", and the author email "bot@factory.example"
    And I push the branch "factory/run" from "/tmp/factory-clone-1" with the token "test-token"
    And I read the head SHA of "/tmp/factory-clone-1"
    And I list the files changed since "main" in "/tmp/factory-clone-1"
    # run: printf 'git'
    Then every CommandSpec sent to the mock names the program "git"
    # run: printf '/tmp/factory-clone-1'
    And every CommandSpec sent to the mock but the clone's has the working directory "/tmp/factory-clone-1"

  @bullet-2
  Scenario: The clone's CommandSpec names the scratch repo URL and holds the token in none of its arguments
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-2"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git'
    Then the clone's CommandSpec has an argument equal to "https://github.com/sbiondolillo/factory-scratch.git"
    # run: printf 'test-token'
    And the clone's CommandSpec has no argument equal to "test-token"

  @bullet-3
  Scenario: The checkout's CommandSpec names checkout, -b and the branch name in that order
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-3"
    And I check out the branch "factory/run" in "/tmp/factory-clone-3"
    # run: printf 'checkout\n-b\nfactory/run'
    Then the checkout's CommandSpec has the arguments "checkout", "-b" and "factory/run", in that order

  @bullet-4
  Scenario: The clone's and the push's CommandSpec each hold the token as FACTORY_GITHUB_TOKEN
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-4"
    And I push the branch "factory/run" from "/tmp/factory-clone-4" with the token "test-token"
    # run: printf 'FACTORY_GITHUB_TOKEN\ntest-token'
    Then the clone's CommandSpec has the environment variable "FACTORY_GITHUB_TOKEN" set to "test-token"
    # run: printf 'FACTORY_GITHUB_TOKEN\ntest-token'
    And the push's CommandSpec has the environment variable "FACTORY_GITHUB_TOKEN" set to "test-token"

  @bullet-5
  Scenario: The clone's and the push's CommandSpec each hold the credential-helper GIT_CONFIG environment variables
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-5"
    And I push the branch "factory/run" from "/tmp/factory-clone-5" with the token "test-token"
    Then the clone's CommandSpec has these environment variables:
      | name               | value             |
      | GIT_CONFIG_COUNT   | 2                 |
      | GIT_CONFIG_KEY_0   | credential.helper |
      | GIT_CONFIG_VALUE_0 |                   |
      | GIT_CONFIG_KEY_1   | credential.helper |
    And the push's CommandSpec has these environment variables:
      | name               | value             |
      | GIT_CONFIG_COUNT   | 2                 |
      | GIT_CONFIG_KEY_0   | credential.helper |
      | GIT_CONFIG_VALUE_0 |                   |
      | GIT_CONFIG_KEY_1   | credential.helper |
    # run: printf 'GIT_CONFIG_VALUE_1\n$FACTORY_GITHUB_TOKEN'
    And the clone's CommandSpec has an environment variable "GIT_CONFIG_VALUE_1" that contains "$FACTORY_GITHUB_TOKEN"
    # run: printf 'GIT_CONFIG_VALUE_1\n$FACTORY_GITHUB_TOKEN'
    And the push's CommandSpec has an environment variable "GIT_CONFIG_VALUE_1" that contains "$FACTORY_GITHUB_TOKEN"

  @bullet-6
  Scenario: The clone's and the push's CommandSpec each hold a credential helper config with username and password placeholders
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-6"
    And I push the branch "factory/run" from "/tmp/factory-clone-6" with the token "test-token"
    # run: printf 'GIT_CONFIG_VALUE_1\nusername=\npassword=$FACTORY_GITHUB_TOKEN'
    Then the clone's CommandSpec has an environment variable "GIT_CONFIG_VALUE_1" that contains "username=" and "password=$FACTORY_GITHUB_TOKEN"
    # run: printf 'GIT_CONFIG_VALUE_1\nusername=\npassword=$FACTORY_GITHUB_TOKEN'
    And the push's CommandSpec has an environment variable "GIT_CONFIG_VALUE_1" that contains "username=" and "password=$FACTORY_GITHUB_TOKEN"

  @bullet-7
  Scenario: The commit's CommandSpec carries the given author as the committer
    Given a Git built over a mock CommandRunner that returns exit code 0 for every command
    When I clone "https://github.com/sbiondolillo/factory-scratch.git" with the token "test-token" into the directory "/tmp/factory-clone-7"
    And I commit all changes in "/tmp/factory-clone-7" with the message "Do the work", the author name "Factory Bot", and the author email "bot@factory.example"
    # run: printf 'GIT_COMMITTER_NAME\nFactory Bot'
    Then the commit's CommandSpec has the environment variable "GIT_COMMITTER_NAME" set to "Factory Bot"
    # run: printf 'GIT_COMMITTER_EMAIL\nbot@factory.example'
    And the commit's CommandSpec has the environment variable "GIT_COMMITTER_EMAIL" set to "bot@factory.example"

  @bullet-8
  Scenario: The head SHA is the 40 characters git prints
    Given a Git built over a mock CommandRunner that returns exit code 0 and standard output "903c6f7ea009814ad6cff2d387717be8f34bab03" for every command
    When I read the head SHA of "/tmp/factory-clone-8"
    # run: printf '903c6f7ea009814ad6cff2d387717be8f34bab03'
    Then the head SHA is "903c6f7ea009814ad6cff2d387717be8f34bab03"

  @bullet-9
  Scenario: The list of changed files holds the file git prints
    Given a Git built over a mock CommandRunner that returns exit code 0 and standard output "README.md" for every command
    When I list the files changed since "main" in "/tmp/factory-clone-9"
    # run: printf 'README.md'
    Then the list of changed files holds "README.md"

  @bullet-10
  Scenario: An unknown base commit makes the changed-files read throw with git's own error
    Given a Git built over a mock CommandRunner that returns exit code 128 and standard error "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree." for every command
    When I list the files changed since "no-such-base" in "/tmp/factory-clone-10"
    # run: printf "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    Then the operation throws an exception whose message holds "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."

  @bullet-11
  Scenario: A push rejected by a concurrent push throws with git's own error
    Given a Git built over a mock CommandRunner that returns exit code 1 and standard error "! [rejected] factory/run -> factory/run (fetch first)" for every command
    When I push the branch "factory/run" from "/tmp/factory-clone-11" with the token "test-token"
    # run: printf '! [rejected] factory/run -> factory/run (fetch first)'
    Then the push throws an exception whose message holds "! [rejected] factory/run -> factory/run (fetch first)"

  @bullet-12
  Scenario: A push with an invalid token throws with an authentication-failure message
    Given a Git built over a mock CommandRunner that returns exit code 128 and standard error "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'" for every command
    When I push the branch "factory/run" from "/tmp/factory-clone-12" with the token "invalid"
    # run: printf 'Authentication failed'
    Then the push throws an exception whose message holds "Authentication failed"
