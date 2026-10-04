Feature: The factory requests the Copilot review of each pull request it watches

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  @bullet-1
  Scenario: Each review ReviewThreads returns holds the author login, the state and the commit
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"
    When a caller reads the reviews of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    # run: printf 'copilot-pull-request-reviewer\nCOMMENTED\nd7d0afd495cc64aa2ac153de8d001ad8d86fe074'
    Then ReviewThreads returns one review with the author "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"

  @bullet-2
  Scenario: A watch with no review of the head commit requests the Copilot review
    Given GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'requestReviewsByLogin\ncopilot-pull-request-reviewer'
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  @bullet-2
  Scenario: A feedback watch with no review of the head commit requests the Copilot review
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    # run: printf 'requestReviewsByLogin\ncopilot-pull-request-reviewer'
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  @bullet-2
  Scenario: A review of an earlier commit only still gets a request
    Given GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2"
    And GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf 'requestReviewsByLogin\ncopilot-pull-request-reviewer'
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  @bullet-2
  Scenario: The request comes after GitHub's head equals the pushed head and before the first read of the check runs
    Given GitHub answers the first read of the head commit of the pull request 10 with "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2" and each later read with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then the review request comes after the read of the head commit that gives the head of the work tree
    And the review request comes before the first read of the check runs

  @bullet-2
  Scenario: With GitHub's head never equal to the pushed head the request comes after the 10-second grace
    Given GitHub answers each read of the head commit of the pull request 10 with "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    # run: printf '10'
    Then the review request comes 10 seconds or more after the push
    And the review request comes before the first read of the check runs

  @bullet-3
  Scenario: A watch with a review of the head commit sends no review request
    Given GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then GitHub gets no review request

  @bullet-3
  Scenario: A feedback watch with a review of the head commit sends no review request
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10"
    Then GitHub gets no review request

  @bullet-4
  Scenario: A GraphQL error on the review request gives a line on standard error and the exit code 1
    Given GitHub answers each read of the reviews with no review
    And GitHub answers the review request with a GraphQL error
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard error holds a line with the message of the GraphQL error
    # run: printf '1'
    And the exit code is 1

  @bullet-5
  Scenario: Every check run passing with a review of the head commit is green with the unresolved threads
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of 1 hour
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success", and the check run "skipped" with the status "completed" and the conclusion "skipped"
    And GitHub answers each read of the reviews with one review by "copilot-pull-request-reviewer" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers the read of the review threads with one unresolved thread with the id "PRRT_kwDOUpm1Qc6obG9o", the path "probe-m19-1790961068.txt", the line 2 and one comment by "sbiondolillo" with the body "Name the unit."
    When a caller starts the watch
    Then the watch gives back a green result
    # run: printf '%s\n' 'PRRT_kwDOUpm1Qc6obG9o'
    And the watch result holds the one thread "PRRT_kwDOUpm1Qc6obG9o"

  @bullet-6
  Scenario: A review of an earlier commit only gives a red result once the timeout passes
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of zero
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with one review by "copilot-pull-request-reviewer" with the state "COMMENTED" of the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-7
  Scenario: No review with a timeout of zero gives a red result
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of zero
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-8
  Scenario: A review by another login with a timeout of zero gives a red result
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of zero
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with one review by "sbiondolillo" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-9
  Scenario: No review at the first poll and a review of the head commit at the second gives green
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of 1 hour
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers the first read of the reviews with no review
    And GitHub answers the second read of the reviews with one review by "copilot-pull-request-reviewer" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a green result

  @bullet-10
  Scenario: The README states the Copilot review request in place of the old line
    When a reader reads the section "## Start a new repo" of README.md
    # run: printf '%s' "The factory requests a Copilot code review of each pull request it watches. The repo's owner needs a Copilot plan that includes Copilot code review."
    Then the section holds the line "The factory requests a Copilot code review of each pull request it watches. The repo's owner needs a Copilot plan that includes Copilot code review."
    # run: printf '%s' 'Copilot code review is requested on every pull request.'
    And the section holds no line "Copilot code review is requested on every pull request."
