Feature: The host shell runs the gates' commands on the runner

  Background:
    Given a HostShell over a new work tree and a mock CommandRunner that records every CommandSpec

  Scenario: A command with no working directory runs in the work tree
    When the host shell runs "dotnet" with the arguments "build --no-restore"
    Then the host shell's CommandSpec has the program "dotnet" and the arguments "build --no-restore"
    And the host shell's CommandSpec runs in the work tree

  Scenario: A command with a working directory runs in that directory
    When the host shell runs "sh" with the arguments "-c true" in its scratch path "check-1"
    Then the host shell's CommandSpec runs in its scratch path "check-1"

  Scenario: A command starts from a cleared environment that holds the allowlist and the passed names
    Given the process environment holds these variables:
      | name                  | value |
      | HOSTSHELL_SPEC_SECRET | s-1   |
      | HOSTSHELL_SPEC_OTHER  | o-1   |
      | DOTNET_HOSTSHELL_SPEC | d-1   |
      | NUGET_HOSTSHELL_SPEC  | n-1   |
    When the host shell runs "dotnet" with the arguments "run --no-restore" and the names "HOSTSHELL_SPEC_SECRET"
    Then the host shell's CommandSpec clears the environment
    And the host shell's CommandSpec holds these environment variables:
      | name                  | value |
      | HOSTSHELL_SPEC_SECRET | s-1   |
      | DOTNET_HOSTSHELL_SPEC | d-1   |
      | NUGET_HOSTSHELL_SPEC  | n-1   |
    And the host shell's CommandSpec holds each of PATH, HOME, TMPDIR and LANG that the process environment holds, with its value
    And every other variable of the host shell's CommandSpec has a name that starts "DOTNET_" or "NUGET_"

  Scenario: A passed name that the process environment lacks is not in the environment of the command
    When the host shell runs "dotnet" with the arguments "run --no-restore" and the names "HOSTSHELL_SPEC_ABSENT"
    Then the host shell's CommandSpec lacks the environment variable "HOSTSHELL_SPEC_ABSENT"

  Scenario: A git program runs with the hooks turned off
    When the host shell runs "git" with the arguments "worktree add --detach /tmp/check-2 HEAD"
    Then the host shell's CommandSpec holds these environment variables:
      | name               | value          |
      | GIT_CONFIG_COUNT   | 1              |
      | GIT_CONFIG_KEY_0   | core.hooksPath |
      | GIT_CONFIG_VALUE_0 | /dev/null      |

  Scenario: A nested git directory in the work tree stops a git program before it runs
    Given the work tree of the host shell holds a directory "vendor/lib/.git"
    When the host shell runs "git" with the arguments "worktree remove --force /tmp/check-3"
    Then the host shell throws a GitCommandException whose message holds "vendor/lib/.git"
    And the mock CommandRunner of the host shell recorded no CommandSpec

  Scenario: A scratch path of the host shell is under the temp directory
    Then the host shell's scratch path for "api-1" is "api-1" under the temp directory of the process

  Scenario: The test gate over a host shell writes the TRX files under the work tree
    When the test gate runs over the host shell with the integration suite skipped
    Then the results directory of the test command is a directory of the work tree whose name starts ".test-gate-"

  Scenario: The lint gate with restore first runs dotnet restore before dotnet format
    When the lint gate runs over the host shell with restore first
    Then the host shell ran "dotnet restore --locked-mode" and then "dotnet format --no-restore"
