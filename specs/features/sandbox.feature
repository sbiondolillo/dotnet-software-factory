Feature: A sandbox container starts, runs commands on the work tree, and is removed

  @bullet-1
  Scenario: A missing image is built before docker run
    Given a Sandbox built over a mock of CommandRunner
    And the root directory holds a global.json whose sdk.version is "10.0.100"
    And docker image inspect exits with the code 1
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    # run: printf -- '--tag\nfactory-sandbox:test'
    Then the runner ran a docker build whose arguments hold "--tag" and then "factory-sandbox:test"
    And the docker build ran before the docker run

  @bullet-2
  Scenario: The container runs as the user id and group id of the caller
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    # run: printf -- '--user'
    Then the arguments of the docker run hold "--user" and then the user id and the group id of the current process joined by a colon

  @bullet-3
  Scenario: The work tree is mounted at /work and is the working directory
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    # run: printf -- '--volume\n/tmp/factory-work-tree:/work\n--workdir\n/work'
    Then the arguments of the docker run hold "--volume" and then "/tmp/factory-work-tree:/work", and "--workdir" and then "/work"

  @bullet-4
  Scenario: The container holds ANTHROPIC_API_KEY by name
    Given a Sandbox built over a mock of CommandRunner
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    # run: printf -- '--env\nANTHROPIC_API_KEY'
    Then the arguments of the docker run hold "--env" and then "ANTHROPIC_API_KEY"

  @bullet-5
  Scenario: A failing docker run makes the start throw a SandboxException
    Given a Sandbox built over a mock of CommandRunner
    And docker run exits with a nonzero code and writes a reason to standard error
    When a caller starts a sandbox with the image tag "factory-sandbox:test" and the work tree "/tmp/factory-work-tree"
    Then the start throws a SandboxException whose message holds the standard error of docker run

  @bullet-6
  Scenario: A command runs through docker exec with the container id
    Given a Sandbox built over a mock of CommandRunner
    And docker run prints a container id of 64 hexadecimal digits and a line feed
    And a caller has started a sandbox
    When a caller runs "dotnet" with the argument "--version" and an empty list of names
    # run: printf -- 'docker\nexec\ndotnet\n--version'
    Then the runner ran a CommandSpec whose program is "docker" and whose first argument is "exec"
    And the last three arguments are the container id with no line feed, "dotnet" and "--version", in that order

  @bullet-7
  Scenario: Each name in the list is passed to docker exec before the container id
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    When a caller runs "printenv" with the names "FIRST_SECRET" and "SECOND_SECRET"
    # run: printf -- '--env\nFIRST_SECRET\n--env\nSECOND_SECRET'
    Then the arguments of the docker exec hold "--env" and then "FIRST_SECRET", and "--env" and then "SECOND_SECRET"
    And each of those is before the container id

  @bullet-8
  Scenario: A name given to one command is not held by the next command
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    When a caller runs "printenv" with the name "FIRST_SECRET"
    And a caller runs "printenv" with an empty list of names
    # run: printf 'FIRST_SECRET'
    Then no argument of the second docker exec is "FIRST_SECRET"

  @bullet-9
  Scenario: A nonzero exit code of the command is given back as a result
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    And docker exec exits with the code 7, writes "out" to standard output and writes "err" to standard error
    When a caller runs "sh" with the argument "-c" and an empty list of names
    # run: printf '7\nout\nerr'
    Then the result has the exit code 7, the standard output "out" and the standard error "err"

  @bullet-10
  Scenario: Disposing the sandbox removes the container
    Given a Sandbox built over a mock of CommandRunner
    And a caller has started a sandbox
    When a caller disposes the sandbox
    # run: printf -- 'docker\nrm\n--force'
    Then the runner ran a CommandSpec whose program is "docker" and whose arguments are "rm", "--force" and the container id, in that order
