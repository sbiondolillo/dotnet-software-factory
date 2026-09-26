Feature: Git clones, branches, commits and pushes

  Background:
    Given a mock of CommandRunner records every CommandSpec it receives
    And Git is built over that mock

  @bullet-1
  Scenario: Every git operation runs as a CommandSpec for the program git, and every operation after the clone runs in the clone's directory
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "ghp_example"
    And Git checks out the branch "factory/run"
    And Git commits every change with the message "test commit", the author name "Ada Lovelace" and the author email "ada@example.com"
    And Git pushes the branch "factory/run" with the token "ghp_example"
    And Git reads the head SHA
    And Git lists the files changed since the base commit "main"
    # run: printf 'git\n/tmp/factory-clone'
    Then every recorded CommandSpec has the program "git"
    And every recorded CommandSpec but the clone's has the working directory "/tmp/factory-clone"

  @bullet-2
  Scenario: The clone's CommandSpec holds the URL in its arguments and holds the token in none of them
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "ghp_example"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git\nghp_example'
    Then the clone's CommandSpec arguments hold "https://github.com/sbiondolillo/factory-scratch.git"
    And no argument of the clone's CommandSpec holds "ghp_example"

  @bullet-3
  Scenario: The clone's and the push's CommandSpec each carry the token as FACTORY_GITHUB_TOKEN
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "ghp_example"
    And Git pushes the branch "factory/run" with the token "ghp_example"
    # run: printf 'FACTORY_GITHUB_TOKEN\nghp_example\nFACTORY_GITHUB_TOKEN\nghp_example'
    Then the clone's CommandSpec environment variable "FACTORY_GITHUB_TOKEN" is "ghp_example"
    And the push's CommandSpec environment variable "FACTORY_GITHUB_TOKEN" is "ghp_example"

  @bullet-4
  Scenario: The clone's and the push's CommandSpec each carry the credential helper through GIT_CONFIG_* variables
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" into "/tmp/factory-clone" with the token "ghp_example"
    And Git pushes the branch "factory/run" with the token "ghp_example"
    # run: printf 'GIT_CONFIG_COUNT\n2\nGIT_CONFIG_KEY_0\ncredential.helper\nGIT_CONFIG_VALUE_0\n\nGIT_CONFIG_KEY_1\ncredential.helper\nGIT_CONFIG_VALUE_1\n$FACTORY_GITHUB_TOKEN'
    Then the clone's CommandSpec environment variable "GIT_CONFIG_COUNT" is "2"
    And the clone's CommandSpec environment variable "GIT_CONFIG_KEY_0" is "credential.helper"
    And the clone's CommandSpec environment variable "GIT_CONFIG_VALUE_0" is ""
    And the clone's CommandSpec environment variable "GIT_CONFIG_KEY_1" is "credential.helper"
    And the clone's CommandSpec environment variable "GIT_CONFIG_VALUE_1" holds "$FACTORY_GITHUB_TOKEN"
    # run: printf 'GIT_CONFIG_COUNT\n2\nGIT_CONFIG_KEY_0\ncredential.helper\nGIT_CONFIG_VALUE_0\n\nGIT_CONFIG_KEY_1\ncredential.helper\nGIT_CONFIG_VALUE_1\n$FACTORY_GITHUB_TOKEN'
    And the push's CommandSpec environment variable "GIT_CONFIG_COUNT" is "2"
    And the push's CommandSpec environment variable "GIT_CONFIG_KEY_0" is "credential.helper"
    And the push's CommandSpec environment variable "GIT_CONFIG_VALUE_0" is ""
    And the push's CommandSpec environment variable "GIT_CONFIG_KEY_1" is "credential.helper"
    And the push's CommandSpec environment variable "GIT_CONFIG_VALUE_1" holds "$FACTORY_GITHUB_TOKEN"

  @bullet-5
  Scenario: The commit's CommandSpec carries the author as GIT_COMMITTER_NAME and GIT_COMMITTER_EMAIL
    When Git commits every change with the message "test commit", the author name "Ada Lovelace" and the author email "ada@example.com"
    # run: printf 'GIT_COMMITTER_NAME\nAda Lovelace\nGIT_COMMITTER_EMAIL\nada@example.com'
    Then the commit's CommandSpec environment variable "GIT_COMMITTER_NAME" is "Ada Lovelace"
    And the commit's CommandSpec environment variable "GIT_COMMITTER_EMAIL" is "ada@example.com"

  @bullet-6
  Scenario: A successful head read gives the 40-character SHA git printed
    Given the mock of CommandRunner answers the head-read command with exit code 0 and standard output "903c6f7ea009814ad6cff2d387717be8f34bab03"
    When Git reads the head SHA
    # run: printf '903c6f7ea009814ad6cff2d387717be8f34bab03'
    Then the head SHA is "903c6f7ea009814ad6cff2d387717be8f34bab03"

  @bullet-7
  Scenario: A successful changed-files read lists the file git printed
    Given the mock of CommandRunner answers the changed-files command with exit code 0 and standard output "README.md"
    When Git lists the files changed since the base commit "main"
    # run: printf 'README.md'
    Then the list of changed files holds "README.md"

  @bullet-8
  Scenario: An unknown base commit makes the changed-files read throw with git's standard error
    Given the mock of CommandRunner answers the changed-files command with exit code 128 and standard error "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    When Git lists the files changed since the base commit "no-such-base"
    # run: printf '%s' "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    Then the operation throws an exception whose message holds "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."

  @bullet-9
  Scenario: A refused push throws with git's standard error
    Given the mock of CommandRunner answers the push command with exit code 1 and standard error "! [rejected] factory/run -> factory/run (fetch first)"
    When Git pushes the branch "factory/run" with the token "ghp_example"
    # run: printf '%s' '! [rejected] factory/run -> factory/run (fetch first)'
    Then the push throws an exception whose message holds "! [rejected] factory/run -> factory/run (fetch first)"

  @bullet-10
  Scenario: A push with an invalid token throws naming the failed authentication
    Given the mock of CommandRunner answers the push command with exit code 128 and standard error "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'"
    When Git pushes the branch "factory/run" with the token "invalid"
    # run: printf '%s' 'Authentication failed'
    Then the push throws an exception whose message holds "Authentication failed"
