Feature: Git clones, branches, commits and pushes with git

  @bullet-1
  Scenario: Every git command names the program git, and each one after the clone runs in the clone's directory
    Given the runner answers every command with exit code 0
    When Git clones the repo "sbiondolillo/factory-scratch" with the token "ghp_test-token" into a work tree
    And Git checks out the branch "factory/run"
    And Git commits every change with the message "Update README", the author name "Ada Factory" and the author email "ada@example.com"
    And Git pushes the branch "factory/run"
    And Git reads the head SHA
    And Git lists the files changed since the base "main"
    # run: printf 'git'
    Then every command the runner ran names the program "git"
    And every command after the clone has the working directory of the clone's work tree

  @bullet-2
  Scenario: The clone command holds the repo URL in its arguments and the token in none of them
    Given Git is configured with the token "ghp_test-token"
    When Git clones the repo "sbiondolillo/factory-scratch" into a work tree
    # run: printf '%s\n%s' 'https://github.com/sbiondolillo/factory-scratch.git' 'ghp_test-token'
    Then the clone command's arguments hold "https://github.com/sbiondolillo/factory-scratch.git"
    And the clone command's arguments hold none of the token "ghp_test-token"

  @bullet-3
  Scenario: The clone and push commands carry the token as an environment variable
    Given Git is configured with the token "ghp_test-token"
    When Git clones the repo "sbiondolillo/factory-scratch" into a work tree
    And Git pushes the branch "factory/run"
    # run: printf '%s\n%s\n%s\n%s' 'FACTORY_GITHUB_TOKEN' 'ghp_test-token' 'FACTORY_GITHUB_TOKEN' 'ghp_test-token'
    Then the clone command's environment variable "FACTORY_GITHUB_TOKEN" is "ghp_test-token"
    And the push command's environment variable "FACTORY_GITHUB_TOKEN" is "ghp_test-token"

  @bullet-4
  Scenario: The clone and push commands set the credential helper through GIT_CONFIG environment variables
    Given Git is configured with the token "ghp_test-token"
    When Git clones the repo "sbiondolillo/factory-scratch" into a work tree
    And Git pushes the branch "factory/run"
    Then the clone command's environment variables hold the following
      | name                | value             |
      | GIT_CONFIG_COUNT    | 2                 |
      | GIT_CONFIG_KEY_0    | credential.helper |
      | GIT_CONFIG_VALUE_0  |                   |
      | GIT_CONFIG_KEY_1    | credential.helper |
    And the push command's environment variables hold the same GIT_CONFIG entries
    # run: printf '%s\n%s\n%s\n%s' 'GIT_CONFIG_VALUE_1' '$FACTORY_GITHUB_TOKEN' 'GIT_CONFIG_VALUE_1' '$FACTORY_GITHUB_TOKEN'
    And the clone command's environment variable "GIT_CONFIG_VALUE_1" holds "$FACTORY_GITHUB_TOKEN"
    And the push command's environment variable "GIT_CONFIG_VALUE_1" holds "$FACTORY_GITHUB_TOKEN"

  @bullet-5
  Scenario: The commit command carries the author as the committer environment
    Given Git is configured with the token "ghp_test-token"
    When Git commits every change with the message "Update README", the author name "Ada Factory" and the author email "ada@example.com"
    # run: printf '%s\n%s\n%s\n%s' 'GIT_COMMITTER_NAME' 'Ada Factory' 'GIT_COMMITTER_EMAIL' 'ada@example.com'
    Then the commit command's environment variable "GIT_COMMITTER_NAME" is "Ada Factory"
    And the commit command's environment variable "GIT_COMMITTER_EMAIL" is "ada@example.com"

  @bullet-6
  Scenario: The head SHA is exactly what git printed
    Given the runner answers the head-read command with exit code 0 and standard output "903c6f7ea009814ad6cff2d387717be8f34bab03"
    When Git reads the head SHA
    # run: printf '903c6f7ea009814ad6cff2d387717be8f34bab03'
    Then the head SHA is "903c6f7ea009814ad6cff2d387717be8f34bab03"

  @bullet-7
  Scenario: The changed-files list holds a file git named
    Given the runner answers the changed-files command with exit code 0 and standard output "README.md"
    When Git lists the files changed since the base "main"
    # run: printf 'README.md'
    Then the list of changed files holds "README.md"

  @bullet-8
  Scenario: An unknown base for the changed-files read throws with git's error
    Given the runner answers the changed-files command with exit code 128 and standard error "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    When Git lists the files changed since the base "no-such-base"
    # run: printf "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."
    Then listing the changed files throws an exception whose message holds "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree."

  @bullet-9
  Scenario: A rejected push throws with git's error
    Given the runner answers the push command with exit code 1 and standard error "! [rejected] factory/run -> factory/run (fetch first)"
    When Git pushes the branch "factory/run"
    # run: printf '%s' '! [rejected] factory/run -> factory/run (fetch first)'
    Then pushing throws an exception whose message holds "! [rejected] factory/run -> factory/run (fetch first)"

  @bullet-10
  Scenario: A failed push authentication throws naming the failure
    Given the runner answers the push command with exit code 128 and standard error "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'"
    When Git pushes the branch "factory/run"
    # run: printf 'Authentication failed'
    Then pushing throws an exception whose message holds "Authentication failed"
