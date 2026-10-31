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
      | GIT_CONFIG_COUNT    | 3                 |
      | GIT_CONFIG_KEY_0    | credential.helper |
      | GIT_CONFIG_VALUE_0  |                   |
      | GIT_CONFIG_KEY_1    | credential.helper |
      | GIT_CONFIG_KEY_2    | core.hooksPath    |
      | GIT_CONFIG_VALUE_2  | /dev/null         |
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

  Scenario: A nested git directory stops a git command before it runs
    Given a work tree in a new directory that holds a directory "sub/.git"
    When Git reads the head SHA in that work tree
    Then Git throws a GitCommandException whose message holds "sub/.git"
    And the mock CommandRunner recorded no CommandSpec

  Scenario: A nested git file stops a git command before it runs
    Given a work tree in a new directory that holds a file "lib/.git"
    When Git reads the diff since "main" in that work tree
    Then Git throws a GitCommandException whose message holds "lib/.git"
    And the mock CommandRunner recorded no CommandSpec

  Scenario: A nested git symlink stops a git command before it runs
    Given a work tree in a new directory that holds a symlink "vendor/.git"
    When Git pushes the branch "feature-1" from that work tree with the token "token-1"
    Then Git throws a GitCommandException whose message holds "vendor/.git"
    And the mock CommandRunner recorded no CommandSpec

  Scenario: The clone appends .factory/ to the exclude file of the clone after the lines it holds
    Given a work tree in a new directory that holds a file ".git/info/exclude" with the lines:
      | line                                                       |
      | # git ls-files --others --exclude-from=.git/info/exclude   |
      | *.swp                                                      |
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-1" into that work tree
    Then the file ".git/info/exclude" in that work tree holds the lines:
      | line                                                       |
      | # git ls-files --others --exclude-from=.git/info/exclude   |
      | *.swp                                                      |
      | .factory/                                                  |

  Scenario: The clone writes .factory/ to a new exclude file when the clone has none
    Given a work tree in a new directory that holds a directory ".git" with no directory "info" in it
    When Git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token "token-1" into that work tree
    Then the file ".git/info/exclude" in that work tree holds the lines:
      | line      |
      | .factory/ |

  Scenario: A git command with no credential helper turns the hooks off in one GIT_CONFIG entry
    When Git checks out the new branch "feature-12" in "/tmp/factory-clone-12"
    Then the "checkout" CommandSpec holds these environment variables:
      | name               | value          |
      | GIT_CONFIG_COUNT   | 1              |
      | GIT_CONFIG_KEY_0   | core.hooksPath |
      | GIT_CONFIG_VALUE_0 | /dev/null      |

  Scenario: The pull fast-forwards from the branch on origin with the credential helper
    When Git pulls the branch "factory/7-session" into "/tmp/factory-clone-13" with the token "token-13"
    Then the "pull" CommandSpec holds the arguments "pull --ff-only origin factory/7-session" and the working directory "/tmp/factory-clone-13"
    And the "pull" CommandSpec holds the environment variable "FACTORY_GITHUB_TOKEN" set to "token-13" and a GIT_CONFIG_VALUE_1 that holds "$FACTORY_GITHUB_TOKEN"

  Scenario: The delete of a remote branch pushes --delete with the credential helper
    When Git deletes the branch "factory/7-session" on origin from "/tmp/factory-clone-14" with the token "token-14"
    Then the "push" CommandSpec holds the arguments "push origin --delete factory/7-session" and the working directory "/tmp/factory-clone-14"
    And the "push" CommandSpec holds the environment variable "FACTORY_GITHUB_TOKEN" set to "token-14" and a GIT_CONFIG_VALUE_1 that holds "$FACTORY_GITHUB_TOKEN"

  Scenario: The remote head is the SHA before the tab on the line that ls-remote prints for the full name of the branch
    Given git answers "ls-remote" with exit code 0 and the standard output "903c6f7ea009814ad6cff2d387717be8f34bab03\trefs/heads/factory/7-session\n"
    When Git reads the remote head of the branch "factory/7-session" in "/tmp/factory-clone-15" with the token "token-15"
    Then the remote head is "903c6f7ea009814ad6cff2d387717be8f34bab03"
    And the "ls-remote" CommandSpec holds the arguments "ls-remote --heads origin refs/heads/factory/7-session" and the working directory "/tmp/factory-clone-15"
    And the "ls-remote" CommandSpec holds the environment variable "FACTORY_GITHUB_TOKEN" set to "token-15" and a GIT_CONFIG_VALUE_1 that holds "$FACTORY_GITHUB_TOKEN"

  Scenario: The remote head is null when origin lacks the branch
    Given git answers "ls-remote" with exit code 0 and the standard output ""
    When Git reads the remote head of the branch "factory/8-session" in "/tmp/factory-clone-16" with the token "token-16"
    Then the remote head is null

  Scenario: The tracked files under a path are the lines that ls-files prints
    Given git answers "ls-files" with exit code 0 and the standard output ".factory/plan.md\n.factory/review.json\n"
    When Git lists the tracked files under ".factory" in "/tmp/factory-clone-17"
    Then the list of tracked files is ".factory/plan.md" and ".factory/review.json"
    And the "ls-files" CommandSpec holds the arguments "ls-files -- .factory" and the working directory "/tmp/factory-clone-17"

  Scenario: No tracked file under a path gives an empty list
    Given git answers "ls-files" with exit code 0 and the standard output ""
    When Git lists the tracked files under ".factory" in "/tmp/factory-clone-18"
    Then the list of tracked files is empty

  Scenario: A pull that git refuses throws a GitCommandException naming pull and holding its standard error
    Given git answers "pull" with exit code 128 and the standard error "fatal: Not possible to fast-forward, aborting."
    When Git pulls the branch "factory/7-session" into "/tmp/factory-clone-19" with the token "token-19"
    Then Git throws a GitCommandException whose message contains "pull"
    And the exception message contains "fatal: Not possible to fast-forward, aborting."

  Scenario: A delete of a branch that origin lacks throws a GitCommandException holding its standard error
    Given git answers "push" with exit code 1 and the standard error "error: unable to delete 'factory/9-session': remote ref does not exist"
    When Git deletes the branch "factory/9-session" on origin from "/tmp/factory-clone-20" with the token "token-20"
    Then Git throws a GitCommandException whose message contains "push"
    And the exception message contains "remote ref does not exist"

  Scenario: A remote head read that git refuses throws a GitCommandException
    Given git answers "ls-remote" with exit code 128 and the standard error "fatal: could not read Username"
    When Git reads the remote head of the branch "factory/7-session" in "/tmp/factory-clone-21" with the token "token-21"
    Then Git throws a GitCommandException whose message contains "ls-remote"
