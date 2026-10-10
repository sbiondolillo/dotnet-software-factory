Feature: The factory waits for a pull request's checks and its Copilot review

  Background:
    Given a PullRequests and a PullRequestWatch built over a GitHub client
    And the watch is for the repo "factory-scratch" of the owner "sbiondolillo", the pull request 565, the reviewer login "sbiondolillo", a wait of zero between two polls and a timeout of 1 hour
    And GitHub answers the read of the pull request with the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "success", and the check run "skipped" with the status "completed" and the conclusion "skipped"
    And GitHub answers each read of the reviews with one review by "sbiondolillo" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    And GitHub answers the read of the review threads with an empty list of threads

  @bullet-1
  Scenario: Reading the head commit sends the GET for the pull request
    When a caller reads the head commit of the pull request 565 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the recorded pull request read has the method "GET" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/pulls/565"

  @bullet-2
  Scenario: Reading the head commit returns the head sha
    When a caller reads the head commit of the pull request 565 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns the head sha "2878cae3e3bab7f3b1dcdfa14206f2c361021085"

  @bullet-3
  Scenario: A 404 on the read of the head commit throws
    Given GitHub answers the read of the pull request with the status code 404
    When a caller reads the head commit of the pull request 565 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests throws a GitHubRequestException that gives the status code 404

  @bullet-4
  Scenario: The watch reads the check runs of the head commit
    When a caller starts the watch
    Then the recorded check runs read has the method "GET" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/commits/2878cae3e3bab7f3b1dcdfa14206f2c361021085/check-runs"

  @bullet-5
  Scenario: Settled with every check run passing is green with no threads
    When a caller starts the watch
    Then the watch gives back a green result
    And the watch result holds an empty list of threads

  @bullet-6
  Scenario: A failed check run is red
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "failure"
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-7
  Scenario: A timeout of zero with a settled pull request is green
    Given the timeout is zero
    When a caller starts the watch
    Then the watch gives back a green result

  @bullet-8
  Scenario: A timeout of zero with a check run in progress is red
    Given the timeout is zero
    And GitHub answers each read of the check runs with the check run "build" with the status "in_progress" and no conclusion
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-9
  Scenario: A timeout of zero with a queued check run is red
    Given the timeout is zero
    And GitHub answers each read of the check runs with the check run "build" with the status "queued" and no conclusion
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-10
  Scenario: A timeout of zero with no check run is red
    Given the timeout is zero
    And GitHub answers each read of the check runs with no check run
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-11
  Scenario: A timeout of zero with no review is red
    Given the timeout is zero
    And GitHub answers each read of the reviews with no review
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-12
  Scenario: A review by another login does not settle the pull request
    Given the timeout is zero
    And the reviewer login is "copilot-pull-request-reviewer"
    When a caller starts the watch
    Then the watch gives back a red result

  @bullet-13
  Scenario: A check run in progress at the first poll and completed at the second gives green
    Given GitHub answers the first read of the check runs with the check run "build" with the status "in_progress" and no conclusion
    And GitHub answers the second read of the check runs with the check run "build" with the status "completed" and the conclusion "success"
    When a caller starts the watch
    Then the watch gives back a green result

  @bullet-14
  Scenario: No check run at the first poll and two completed check runs at the second gives green
    Given GitHub answers the first read of the check runs with no check run
    And GitHub answers the second read of the check runs with the check run "build" with the status "completed" and the conclusion "success", and the check run "skipped" with the status "completed" and the conclusion "skipped"
    When a caller starts the watch
    Then the watch gives back a green result

  @bullet-15
  Scenario: No review at the first poll and a review at the second gives green
    Given GitHub answers the first read of the reviews with no review
    And GitHub answers the second read of the reviews with one review by "sbiondolillo" with the state "COMMENTED" of the commit "2878cae3e3bab7f3b1dcdfa14206f2c361021085"
    When a caller starts the watch
    Then the watch gives back a green result

  @bullet-16
  Scenario: An unresolved thread is returned with its id, path, line and comment
    Given GitHub answers the read of the review threads with one unresolved thread with the id "PRRT_kwDOUpm1Qc6obG9o", the path "probe-m19-1790961068.txt", the line 2 and one comment by "sbiondolillo" with the body "Name the unit."
    When a caller starts the watch
    Then the watch gives back a green result
    And the watch result holds one thread with the id "PRRT_kwDOUpm1Qc6obG9o", the path "probe-m19-1790961068.txt", the line 2 and one comment with the author "sbiondolillo" and the body "Name the unit."

  @bullet-17
  Scenario: A resolved thread is left out of the result
    Given GitHub answers the read of the review threads with the resolved thread "PRRT_kwDOUpm1Qc6nMu0Y" and the unresolved thread "PRRT_kwDOUpm1Qc6obG9o"
    When a caller starts the watch
    Then the watch result holds the one thread "PRRT_kwDOUpm1Qc6obG9o"

  @bullet-18
  Scenario: A red result holds the unresolved threads
    Given GitHub answers each read of the check runs with the check run "build" with the status "completed" and the conclusion "failure"
    And GitHub answers the read of the review threads with the unresolved thread "PRRT_kwDOUpm1Qc6obG9o"
    When a caller starts the watch
    Then the watch gives back a red result
    And the watch result holds the thread "PRRT_kwDOUpm1Qc6obG9o"

  @bullet-19
  Scenario: A red result from the timeout holds the unresolved threads
    Given the timeout is zero
    And GitHub answers each read of the reviews with no review
    And GitHub answers the read of the review threads with the unresolved thread "PRRT_kwDOUpm1Qc6obG9o"
    When a caller starts the watch
    Then the watch gives back a red result
    And the watch result holds the thread "PRRT_kwDOUpm1Qc6obG9o"

  @bullet-20
  Scenario: A 404 on the read of the pull request ends the watch with an exception
    Given GitHub answers the read of the pull request with the status code 404
    When a caller starts the watch
    Then the watch throws a GitHubRequestException that gives the status code 404

  @bullet-21
  Scenario: Cancelling during the wait throws OperationCanceledException
    Given the wait between two polls is 1 hour
    And GitHub answers each read of the check runs with the check run "build" with the status "in_progress" and no conclusion
    When a caller starts the watch and cancels the cancellation token 1 second after the start
    Then the watch throws an OperationCanceledException
