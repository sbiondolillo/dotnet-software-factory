Feature: The console program runs the factory on an issue or a pull request

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  @bullet-1
  Scenario: run help
    When the program starts with the arguments "run --help"
    # run: printf -- '--repo\n--issue\n--pass-env\n--timeout\n60'
    Then standard output holds "--repo", "--issue", "--pass-env", "--timeout" and "60"
    And GitHub gets no request
    # run: printf '0'
    Then the exit code is 0

  @bullet-2
  Scenario: feedback help
    When the program starts with the arguments "feedback --help"
    # run: printf -- '--repo\n--pr\n--pass-env\n--timeout'
    Then standard output holds "--repo", "--pr", "--pass-env" and "--timeout"
    And GitHub gets no request
    # run: printf '0'
    Then the exit code is 0

  @bullet-3
  Scenario: empty arguments
    When the program starts with the arguments ""
    Then standard error holds text
    And GitHub gets no request
    And no program runs
    # run: printf '1'
    Then the exit code is 1

  @bullet-4
  Scenario: unknown verb
    When the program starts with the arguments "build --repo sbiondolillo/factory-scratch"
    Then standard error holds text
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-5
  Scenario: run without repo
    When the program starts with the arguments "run --issue 12"
    Then standard error holds text
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-6
  Scenario: run without issue
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch"
    Then standard error holds text
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-7
  Scenario: feedback without pr
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch"
    Then standard error holds text
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-8
  Scenario: repo without slash
    When the program starts with the arguments "run --repo factory-scratch --issue 12"
    Then standard error holds text
    And GitHub gets no request
    # run: printf '1'
    Then the exit code is 1

  @bullet-9
  Scenario: run lacks token
    Given the environment lacks "FACTORY_GITHUB_TOKEN"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'FACTORY_GITHUB_TOKEN'
    Then standard error holds one line that holds "FACTORY_GITHUB_TOKEN"
    And GitHub gets no request
    And no program runs
    # run: printf '1'
    Then the exit code is 1

  @bullet-10
  Scenario: run lacks key
    Given the environment lacks "ANTHROPIC_API_KEY"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'ANTHROPIC_API_KEY'
    Then standard error holds one line that holds "ANTHROPIC_API_KEY"
    And GitHub gets no request
    And no program runs
    # run: printf '1'
    Then the exit code is 1

  @bullet-11
  Scenario: feedback lacks token
    Given the environment lacks "FACTORY_GITHUB_TOKEN"
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf 'FACTORY_GITHUB_TOKEN'
    Then standard error holds one line that holds "FACTORY_GITHUB_TOKEN"
    And GitHub gets no request
    And no program runs
    # run: printf '1'
    Then the exit code is 1

  @bullet-12
  Scenario: repo read and clone
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '/repos/sbiondolillo/factory-scratch'
    Then GitHub gets a GET for the path "/repos/sbiondolillo/factory-scratch"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git'
    And git clones "https://github.com/sbiondolillo/factory-scratch.git" with the token of FACTORY_GITHUB_TOKEN

  @bullet-13
  Scenario: clone in temp dir
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then git clones into a directory under the temporary directory of the system
    And that directory is gone when the program exits

  @bullet-14
  Scenario: default branch trunk
    Given GitHub answers the read of the repo with the default branch "trunk"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'trunk'
    Then the pull request opens with the base "trunk"

  @bullet-15
  Scenario: repo 404
    Given GitHub answers the read of the repo with the status code 404
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '404'
    Then standard error holds one line that holds "404"
    And git clones no repo
    # run: printf '1'
    And the exit code is 1

  @bullet-16
  Scenario: branch name
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '8'
    Then git pushes one branch whose name is factory/ and 8 lowercase hexadecimal characters

  @bullet-17
  Scenario: two starts differ
    When the program starts twice with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then git pushes two branches with different names

  @bullet-18
  Scenario: models and turns
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'claude-sonnet-5-5\n50\nclaude-sonnet-5-5\n200\nclaude-sonnet-5-5\n50'
    Then claude runs the plan prompt with the model "claude-sonnet-5-5" and the turn limit 50, the build prompt with the model "claude-sonnet-5-5" and the turn limit 200, and the review prompt with the model "claude-sonnet-5-5" and the turn limit 50

  @bullet-19
  Scenario: author
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'dotnet-software-factory\ndotnet-software-factory@noreply.github.com'
    Then git commits with the author name "dotnet-software-factory" and the author email "dotnet-software-factory@noreply.github.com"

  @bullet-20
  Scenario: docker image
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'dotnet-software-factory-sandbox'
    Then docker starts the container from the image "dotnet-software-factory-sandbox"

  @bullet-21
  Scenario: image build
    Given docker answers image inspect with the exit code 1
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'dotnet-software-factory-sandbox'
    Then docker builds the image "dotnet-software-factory-sandbox" with the current directory of the program as its context

  @bullet-22
  Scenario: pass env run
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --pass-env WEATHER_API_KEY --pass-env MAPS_API_KEY"
    # run: printf 'WEATHER_API_KEY\nMAPS_API_KEY\ndotnet run --no-restore --project tests/Integration'
    Then docker gives the names "WEATHER_API_KEY" and "MAPS_API_KEY" to the command "dotnet run --no-restore --project tests/Integration"

  @bullet-23
  Scenario: pass env feedback
    Given GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --pass-env WEATHER_API_KEY"
    # run: printf 'WEATHER_API_KEY\ndotnet run --no-restore --project tests/Integration'
    Then docker gives the name "WEATHER_API_KEY" to the command "dotnet run --no-restore --project tests/Integration"

  @bullet-24
  Scenario: no solution
    Given the clone lacks Solution.slnx
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '12'
    Then the program posts one comment on the issue 12
    And GitHub gets no read of the check runs
    # run: printf '1'
    And the exit code is 1

  @bullet-25
  Scenario: pr url
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch/pull/10'
    Then standard output holds the line "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-26
  Scenario: check runs read
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '/repos/sbiondolillo/factory-scratch/commits/4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c/check-runs'
    Then GitHub gets a GET for the path "/repos/sbiondolillo/factory-scratch/commits/4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c/check-runs"

  @bullet-27
  Scenario: old head then new
    Given GitHub answers the first read of the head commit of the pull request 10 with "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2" and each later read with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c\n9e107d9d372bb6826bd81d3542a419d6f0a3c1b2'
    Then GitHub gets a read of the check runs of "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub gets no read of the check runs of "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2"

  @bullet-28
  Scenario: old head always
    Given GitHub answers each read of the head commit of the pull request 10 with "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '9e107d9d372bb6826bd81d3542a419d6f0a3c1b2\n10'
    Then GitHub gets a read of the check runs of "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2" 10 seconds or more after the push

  @bullet-29
  Scenario: in progress then success
    Given GitHub answers the first read of the check runs with the check run "build" with the status "in_progress" and no conclusion
    And GitHub answers each later read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '30'
    Then the second read of the check runs comes 30 seconds or more after the first
    # run: printf '0'
    And the exit code is 0

  @bullet-30
  Scenario: foreign review timeout 0
    Given GitHub answers each read of the reviews with one review with the author login "sbiondolillo" and the state "COMMENTED"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --timeout 0"
    # run: printf '1'
    Then the exit code is 1
    # run: printf 'checks: red, unresolved threads: 0'
    And standard output holds the line "checks: red, unresolved threads: 0"

  @bullet-31
  Scenario: green
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'checks: green, unresolved threads: 0\n0\n1'
    Then standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0
    And git clones 1 time in the run

  @bullet-32
  Scenario: red check
    Given GitHub answers each read of the check runs with a red check
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'checks: red, unresolved threads: 0\n1\n1'
    Then standard output holds the line "checks: red, unresolved threads: 0"
    And the exit code is 1
    And git clones 1 time in the run

  @bullet-33
  Scenario: second clone
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git\nfactory/a1b2c3d4'
    Then git clones "https://github.com/sbiondolillo/factory-scratch.git" a second time and checks out the branch "factory/a1b2c3d4" in it

  @bullet-34
  Scenario: feedback build prompt
    Given GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'claude-sonnet-5-5\n200\nPRRT_kwDOUpm1Qc6ofWu5\nThe total ignores the second item of the cart.'
    Then the feedback run's build prompt runs with the model "claude-sonnet-5-5" and the turn limit 200, and holds "PRRT_kwDOUpm1Qc6ofWu5" and "The total ignores the second item of the cart."

  @bullet-35
  Scenario: resolves
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'PRRT_kwDOUpm1Qc6ofWu5\nchecks: green, unresolved threads: 0\n0'
    Then the program resolves the thread "PRRT_kwDOUpm1Qc6ofWu5"
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  @bullet-36
  Scenario: checks after reply
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then GitHub gets a read of the check runs after the last reply

  @bullet-37
  Scenario: disputed
    Given GitHub answers each read of the review threads with the thread A
    And the agent writes the disputed report
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'checks: green, unresolved threads: 1\n1\n2'
    Then standard output holds the line "checks: green, unresolved threads: 1"
    And the exit code is 1
    And git clones 2 times in the run

  @bullet-38
  Scenario: red and thread
    Given GitHub answers each read of the check runs with a red check
    And GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '2'
    Then git clones 2 times in the run

  @bullet-39
  Scenario: feedback clones
    Given GitHub answers each read of the review threads with the thread A
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git\nfactory/a1b2c3d4'
    Then git clones "https://github.com/sbiondolillo/factory-scratch.git" and checks out the branch "factory/a1b2c3d4" in it
    And claude runs no plan prompt

  @bullet-40
  Scenario: feedback no threads
    Given GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then git clones no repo
    # run: printf 'checks: green, unresolved threads: 0\n0'
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  @bullet-41
  Scenario: feedback resolves
    Given GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf 'PRRT_kwDOUpm1Qc6ofWu5\nchecks: green, unresolved threads: 0\n0'
    Then the program resolves the thread "PRRT_kwDOUpm1Qc6ofWu5"
    And standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  @bullet-42
  Scenario: feedback red
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the check runs with a red check
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf 'checks: red, unresolved threads: 1\n1'
    Then standard output holds the line "checks: red, unresolved threads: 1"
    And the exit code is 1

  @bullet-43
  Scenario: four red builds
    Given GitHub answers each read of the review threads with the thread A
    And the test gate is red after each of four builds
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf '10'
    Then the program posts one comment on the pull request 10
    # run: printf '1'
    And the exit code is 1

  @bullet-44
  Scenario: feedback timeout 0
    Given GitHub answers each read of the review threads with no thread
    And GitHub answers each read of the check runs with no check run
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --timeout 0"
    # run: printf 'checks: red, unresolved threads: 0\n1'
    Then standard output holds the line "checks: red, unresolved threads: 0"
    And the exit code is 1
