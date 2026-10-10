Feature: ReviewThreads sends a GraphQL request again when GitHub fails for a moment

  @bullet-1
  Scenario: A read of threads that fails once gives back the thread after 2 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers first with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-07T06:38:10Z. Please include `440A:32D753:9B336D:1FD698D:6AC5E8D1` when reporting this issue."
    And GitHub then answers the read of threads with one unresolved thread with the id "PRRT_1"
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns one thread with the id "PRRT_1"
    And GitHub received 2 requests

  @bullet-2
  Scenario: A read of reviews that fails once gives back the review after 2 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers first with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-03T13:17:42Z. Please include `1408:23AADB:6EFD7:1707EA:6AC10075` when reporting this issue."
    And GitHub then answers the read of reviews with one review with the author login "sbiondolillo"
    When a caller reads the reviews of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns one review with the author "sbiondolillo"
    And GitHub received 2 requests

  @bullet-3
  Scenario: A review request that fails once returns with no exception after 2 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers first with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-07T06:38:10Z. Please include `440A:32D753:9B336D:1FD698D:6AC5E8D1` when reporting this issue."
    And GitHub then answers with a good answer
    When a caller requests a review on pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then RequestReviewAsync returns with no exception
    And GitHub received 2 requests

  @bullet-4
  Scenario: A reply that fails once returns with no exception after 2 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers first with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-07T06:38:10Z. Please include `440A:32D753:9B336D:1FD698D:6AC5E8D1` when reporting this issue."
    And GitHub then answers with a good answer
    When a caller replies to the thread "PRRT_1" with the text "The timeout is in seconds."
    Then ReplyAsync returns with no exception
    And GitHub received 2 requests

  @bullet-5
  Scenario: A resolve that fails once returns with no exception after 2 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers first with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-07T06:38:10Z. Please include `440A:32D753:9B336D:1FD698D:6AC5E8D1` when reporting this issue."
    And GitHub then answers with a good answer
    When a caller resolves the thread "PRRT_1"
    Then ResolveAsync returns with no exception
    And GitHub received 2 requests

  @bullet-6
  Scenario: A read of threads that fails on every request throws after 4 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers every request with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-07T06:38:10Z. Please include `440A:32D753:9B336D:1FD698D:6AC5E8D1` when reporting this issue."
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads throws a GitHubGraphQLException
    And the message of the exception holds "Something went wrong while executing your query"
    And GitHub received 4 requests

  @bullet-7
  Scenario: A fault then another message throws after 2 requests
    Given a ReviewThreads built over a GitHub client
    And GitHub answers first with the status 200 and a list errors holding the message "Something went wrong while executing your query on 2026-10-07T06:38:10Z. Please include `440A:32D753:9B336D:1FD698D:6AC5E8D1` when reporting this issue."
    And GitHub then answers with the status 200 and a list errors holding the message "Could not resolve to a PullRequest with the number of 0."
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads throws a GitHubGraphQLException
    And the message of the exception holds "Could not resolve to a PullRequest"
    And GitHub received 2 requests

