Feature: A sandbox container starts, runs commands on the work tree, and is removed

  Scenario: A missing image is built before docker run
    Given a Sandbox built over a mock of CommandRunner
    And the root directory holds a global.json whose sdk.version is "10.0.100"
    And docker image inspect exits with the code 1
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the runner ran a docker build whose arguments hold "--tag" and then "factory-sandbox:test"
    And the docker build ran before the docker run

  Scenario: The container runs as the user id and group id of the caller
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the arguments of the docker run hold "--user" and then the user id and the group id of the current process joined by a colon

  Scenario: The work tree is mounted at /work and is the working directory
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the arguments of the docker run hold "--volume" and then "/tmp/factory-work-tree:/work", and "--workdir" and then "/work"

  Scenario: The container holds ANTHROPIC_API_KEY by name
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the arguments of the docker run hold "--env" and then "ANTHROPIC_API_KEY"

  Scenario: The container holds ANTHROPIC_BASE_URL by name
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the arguments of the docker run hold "--env" and then "ANTHROPIC_BASE_URL"

  Scenario: A failing docker run makes the start throw a SandboxException
    Given a Sandbox built over a mock of CommandRunner
    And docker run exits with a nonzero code and writes a reason to standard error
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the start throws a SandboxException whose message holds the standard error of docker run

  Scenario: A command runs through docker exec with the container id
    Given a Sandbox built over a mock of CommandRunner
    And docker run prints a container id of 64 hexadecimal digits and a line feed
    And a caller has started a sandbox
    When a caller runs "dotnet" with the argument "--version" and an empty list of names
    Then the runner ran a CommandSpec whose program is "docker" and whose first argument is "exec"
    And the last three arguments are the container id with no line feed, "dotnet" and "--version", in that order

  Scenario: Each name in the list is passed to docker exec before the container id
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    When a caller runs "printenv" with the names "FIRST_SECRET" and "SECOND_SECRET"
    Then the arguments of the docker exec hold "--env" and then "FIRST_SECRET", and "--env" and then "SECOND_SECRET"
    And each of those is before the container id

  Scenario: A name given to one command is not held by the next command
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    When a caller runs "printenv" with the name "FIRST_SECRET"
    And a caller runs "printenv" with an empty list of names
    Then no argument of the second docker exec is "FIRST_SECRET"

  Scenario: A nonzero exit code of the command is given back as a result
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    And docker exec exits with the code 7, writes "out" to standard output and writes "err" to standard error
    When a caller runs "sh" with the argument "-c" and an empty list of names
    Then the result has the exit code 7, the standard output "out" and the standard error "err"

  Scenario: Disposing the sandbox removes the container
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    When a caller disposes the sandbox
    Then the runner ran a CommandSpec whose program is "docker" and whose arguments are "rm", "--force" and the container id, in that order

  Scenario: The git directory of the work tree is mounted read-only
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a directory ".git"
    When a caller starts a sandbox with the image tag "factory-sandbox:test" over that work tree
    Then the arguments of the docker run hold "--volume" and then the path of that work tree followed by "/.git:/work/.git:ro"

  Scenario: The worktrees directory of the git directory stays writable
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a directory ".git"
    When a caller starts a sandbox with the image tag "factory-sandbox:test" over that work tree
    Then the arguments of the docker run hold "--volume" and then the path of that work tree followed by "/.git/worktrees:/work/.git/worktrees"

  Scenario: A missing worktrees directory is created before the docker run
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a directory ".git" with no directory "worktrees" in it
    When a caller starts a sandbox with the image tag "factory-sandbox:test" over that work tree
    Then the directory ".git/worktrees" of that work tree existed when the docker run started

  Scenario: The home of the container is a new directory under .factory/sandbox-home of the root directory
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the arguments of the docker run hold "--volume" and then the path of a directory under ".factory/sandbox-home/" of the root directory followed by ":/tmp/sandbox-home"
    And the arguments of the docker run hold "--env" and then "HOME=/tmp/sandbox-home"
    And no argument of the docker run is "--tmpfs"

  Scenario: The home directory exists on the host when the docker run starts
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the home directory of that sandbox existed when the docker run started

  Scenario: Two sandboxes get two home directories
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    And a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the two docker runs mount two different home directories

  Scenario: A transcript the container wrote into its home is on the host after the sandbox is removed
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    And docker exec writes the file ".claude/projects/-work/session.jsonl" into the mounted home
    When a caller runs "claude" with the argument "-p" and an empty list of names
    And a caller disposes the sandbox
    Then the file ".claude/projects/-work/session.jsonl" is under ".factory/sandbox-home/" of the root directory

  Scenario: A sandbox for the gates holds no Anthropic variable
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a directory ".git"
    When a caller starts a sandbox for the gates with the image tag "factory-sandbox:test" over that work tree
    Then the arguments of the docker run hold neither "ANTHROPIC_API_KEY" nor "ANTHROPIC_BASE_URL"
    And the arguments of the docker run hold "--volume" and then the path of that work tree followed by ":/work"

  Scenario: A sandbox for the gates mounts .factory and specs read-only
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a directory ".git"
    When a caller starts a sandbox for the gates with the image tag "factory-sandbox:test" over that work tree
    Then the arguments of the docker run hold "--volume" and then the path of that work tree followed by "/.factory:/work/.factory:ro"
    And the arguments of the docker run hold "--volume" and then the path of that work tree followed by "/specs:/work/specs:ro"
    And the directory ".factory" of that work tree existed when the docker run started
    And the directory "specs" of that work tree existed when the docker run started

  Scenario: A sandbox of the Claude Code path keeps .factory writable
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a directory ".git"
    When a caller starts a sandbox with the image tag "factory-sandbox:test" over that work tree
    Then no argument of the docker run ends with ".factory:/work/.factory:ro"

  Scenario Outline: A sandbox for the gates refuses a symbolic link in a read-only path
    Given a Sandbox built over a mock of CommandRunner
    And a work tree in a new directory that holds a symlink "<link>"
    When a caller starts a sandbox for the gates with the image tag "factory-sandbox:test" over that work tree
    Then the start throws a SandboxException whose message holds "<link>"
    And the runner ran no docker run

    Examples:
      | link     |
      | .factory |
      | specs    |
