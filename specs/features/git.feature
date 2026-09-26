Feature: Git

  @bullet-1
  Scenario: The clone runs as the git program
    Given a Git built over a mock CommandRunner
    When the factory clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "test-token"
    # run: printf 'git'
    Then the CommandRunner received a CommandSpec whose program is "git"

  @bullet-1
  Scenario: An operation after the clone uses the clone's directory as its working directory
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    When the factory checks out the branch "factory/run"
    # run: printf '/tmp/factory-clone'
    Then the CommandRunner received a CommandSpec whose working directory is "/tmp/factory-clone"

  @bullet-2
  Scenario: The clone command names the repository URL and keeps the token out of its arguments
    Given a Git built over a mock CommandRunner
    When the factory clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "test-token"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git'
    Then the CommandRunner received a CommandSpec whose arguments include "https://github.com/sbiondolillo/factory-scratch.git"
    # run: printf 'test-token'
    And the CommandRunner received a CommandSpec whose arguments do not include "test-token"

  @bullet-3
  Scenario: The checkout command names the new branch
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    When the factory checks out the branch "factory/run"
    # run: printf '%s\n' checkout -b factory/run
    Then the CommandRunner received a CommandSpec whose arguments are "checkout", "-b" and "factory/run" in that order

  @bullet-4
  Scenario: The clone command carries the token as the FACTORY_GITHUB_TOKEN environment variable
    Given a Git built over a mock CommandRunner
    When the factory clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "test-token"
    # run: printf '%s\n' FACTORY_GITHUB_TOKEN test-token
    Then the CommandRunner received a CommandSpec whose environment variable "FACTORY_GITHUB_TOKEN" is "test-token"

  @bullet-4
  Scenario: The push command carries the token as the FACTORY_GITHUB_TOKEN environment variable
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    When the factory pushes the branch "factory/run"
    # run: printf '%s\n' FACTORY_GITHUB_TOKEN test-token
    Then the CommandRunner received a CommandSpec whose environment variable "FACTORY_GITHUB_TOKEN" is "test-token"

  @bullet-5
  Scenario: The clone command clears the machine's credential helper and installs the token helper
    Given a Git built over a mock CommandRunner
    When the factory clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "test-token"
    Then the CommandRunner received a CommandSpec whose environment variables include:
      | name               | value              |
      | GIT_CONFIG_COUNT   | 2                  |
      | GIT_CONFIG_KEY_0   | credential.helper  |
      | GIT_CONFIG_VALUE_0 |                    |
      | GIT_CONFIG_KEY_1   | credential.helper  |
    # run: printf '%s\n' GIT_CONFIG_VALUE_1 '$FACTORY_GITHUB_TOKEN'
    And the CommandRunner received a CommandSpec whose environment variable "GIT_CONFIG_VALUE_1" contains "$FACTORY_GITHUB_TOKEN"

  @bullet-5
  Scenario: The push command clears the machine's credential helper and installs the token helper
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    When the factory pushes the branch "factory/run"
    Then the CommandRunner received a CommandSpec whose environment variables include:
      | name               | value              |
      | GIT_CONFIG_COUNT   | 2                  |
      | GIT_CONFIG_KEY_0   | credential.helper  |
      | GIT_CONFIG_VALUE_0 |                    |
      | GIT_CONFIG_KEY_1   | credential.helper  |
    # run: printf '%s\n' GIT_CONFIG_VALUE_1 '$FACTORY_GITHUB_TOKEN'
    And the CommandRunner received a CommandSpec whose environment variable "GIT_CONFIG_VALUE_1" contains "$FACTORY_GITHUB_TOKEN"

  @bullet-6
  Scenario: The commit command carries the given author as its committer
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    When the factory commits every change with the message "Update README", the author name "Ada Lovelace" and the author email "ada@example.com"
    # run: printf '%s\n' GIT_COMMITTER_NAME 'Ada Lovelace'
    Then the CommandRunner received a CommandSpec whose environment variable "GIT_COMMITTER_NAME" is "Ada Lovelace"
    # run: printf '%s\n' GIT_COMMITTER_EMAIL ada@example.com
    And the CommandRunner received a CommandSpec whose environment variable "GIT_COMMITTER_EMAIL" is "ada@example.com"

  @bullet-7
  Scenario: The head SHA is exactly what git printed
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    And the CommandRunner answers the head read with exit code 0 and standard output "903c6f7ea009814ad6cff2d387717be8f34bab03"
    When the factory reads the head SHA
    # run: printf '903c6f7ea009814ad6cff2d387717be8f34bab03'
    Then the head SHA is "903c6f7ea009814ad6cff2d387717be8f34bab03"

  @bullet-8
  Scenario: The changed files list holds a file git named
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    And the CommandRunner answers the changed-files read with exit code 0 and standard output "README.md"
    When the factory lists the files changed since "main"
    # run: printf 'README.md'
    Then the list of changed files holds "README.md"

  @bullet-9
  Scenario: An unknown base revision throws an exception holding git's standard error
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    And the CommandRunner answers the changed-files read with exit code 128 and standard error "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    When the factory lists the files changed since "no-such-base"
    # run: printf '%s' "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    Then the operation throws an exception whose message contains "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."

  @bullet-10
  Scenario: A rejected push throws an exception holding git's standard error
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "test-token"
    And the CommandRunner answers the push with exit code 1 and standard error "! [rejected] factory/run -> factory/run (fetch first)"
    When the factory pushes the branch "factory/run"
    # run: printf '%s' '! [rejected] factory/run -> factory/run (fetch first)'
    Then the push throws an exception whose message contains "! [rejected] factory/run -> factory/run (fetch first)"

  @bullet-11
  Scenario: A push with a failed token throws an exception naming the authentication failure
    Given a Git built over a mock CommandRunner cloning into "/tmp/factory-clone" with the token "invalid"
    And the CommandRunner answers the push with exit code 128 and standard error "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'"
    When the factory pushes the branch "factory/run"
    # run: printf 'Authentication failed'
    Then the push throws an exception whose message contains "Authentication failed"
