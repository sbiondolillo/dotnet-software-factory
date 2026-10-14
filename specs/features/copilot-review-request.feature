Feature: With --copilot-review on, the factory requests the Copilot review of each pull request it watches

  Background:
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the clone holds the four paths of the layout

  Scenario: Each review ReviewThreads returns holds the author login, the state and the commit
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"
    When a caller reads the reviews of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns one review with the author "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"

  Scenario: A watch with no review of the head commit requests the Copilot review
    Given GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  Scenario: A feedback watch with no review of the head commit requests the Copilot review
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  Scenario: A review of an earlier commit only still gets a request
    Given GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2"
    And GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  Scenario: The request comes after GitHub's head equals the pushed head and before the first read of the check runs
    Given GitHub answers the first read of the head commit of the pull request 10 with "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2" and each later read with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then the review request comes after the read of the head commit that gives the head of the work tree
    And the review request comes before the first read of the check runs

  Scenario: With GitHub's head never equal to the pushed head the request comes after the 10-second grace
    Given GitHub answers each read of the head commit of the pull request 10 with "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2"
    And GitHub answers each read of the reviews with no review
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then the review request comes 10 seconds or more after the push
    And the review request comes before the first read of the check runs

  Scenario: A watch with a review of the head commit sends no review request
    Given GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then GitHub gets no review request

  Scenario: A feedback watch with a review of the head commit sends no review request
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED" and the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --copilot-review on"
    Then GitHub gets no review request

  Scenario: A GraphQL error on the review request gives a line on standard error and the exit code 1
    Given GitHub answers each read of the reviews with no review
    And GitHub answers the review request with a GraphQL error
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then standard error holds a line with the message of the GraphQL error
    And the exit code is 1

  Scenario: Every check run passing with a review of the head commit is green with the unresolved threads
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of 1 hour
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success", and the check run "skipped" with the status "completed" and the conclusion "skipped"
    And GitHub answers each read of the reviews with one review by "copilot-pull-request-reviewer" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers the read of the review threads with one unresolved thread with the id "PRRT_kwDOUpm1Qc6obG9o", the path "probe-m19-1790961068.txt", the line 2 and one comment by "sbiondolillo" with the body "Name the unit."
    When a caller starts the watch
    Then the watch gives back a green result
    And the watch result holds the one thread "PRRT_kwDOUpm1Qc6obG9o"

  Scenario: A review of an earlier commit only gives a red result once the timeout passes
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of zero
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with one review by "copilot-pull-request-reviewer" with the state "COMMENTED" of the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a red result

  Scenario: No review with a timeout of zero gives a red result
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of zero
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with no review
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a red result

  Scenario: A review by another login with a timeout of zero gives a red result
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "copilot-pull-request-reviewer", a wait of zero between two polls and a timeout of zero
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    And GitHub answers each read of the reviews with one review by "sbiondolillo" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers the read of the review threads with an empty list of threads
    When a caller starts the watch
    Then the watch gives back a red result

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

  Scenario: A feedback watch with a refusal of the head commit gets a new review request
    Given GitHub answers each read of the review threads with the thread A
    And GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED", the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and the body "Copilot was unable to review this pull request because the user who requested the review has reached their quota limit."
    When the program starts with the arguments "feedback --repo sbiondolillo/factory-scratch --pr 10 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"

  Scenario: A refusal before the new review request and a review of the head commit after it is green
    Given GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews before the review request and the first read after it with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED", the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and the body "Copilot was unable to review this pull request because the user who requested the review has reached their quota limit.", and each later read with that review and a second review with the author login "copilot-pull-request-reviewer", the state "COMMENTED", the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and the body "<!-- ccr-overview-v2 -->"
    And GitHub answers each read of the review threads with no thread
    And GitHub answers each read of the check runs with a green check
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then standard output holds the line "checks: green, unresolved threads: 0"
    And the exit code is 0

  Scenario: A refusal before the new review request and a second refusal after it is red with the refusal
    Given GitHub answers each read of the head commit of the pull request 10 with "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And GitHub answers each read of the reviews before the review request and the first read after it with one review with the author login "copilot-pull-request-reviewer", the state "COMMENTED", the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and the body "Copilot was unable to review this pull request because the user who requested the review has reached their quota limit.", and each later read with that review and a second review with the author login "copilot-pull-request-reviewer", the state "COMMENTED", the commit "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and the body "Copilot was unable to review this pull request because the user who requested the review has reached their quota limit."
    And GitHub answers each read of the review threads with no thread
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12 --copilot-review on"
    Then GitHub gets one GraphQL request that names "requestReviewsByLogin" with the bot login "copilot-pull-request-reviewer"
    And standard output holds the line "review: refused: Copilot was unable to review this pull request because the user who requested the review has reached their quota limit."
    And standard output holds the line "checks: red, unresolved threads: 0"
    And the exit code is 1
