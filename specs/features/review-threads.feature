Feature: Review threads

  Scenario: Reading the reviews sends the GraphQL POST with the owner, name and number
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of reviews with an empty list of reviews
    When a caller reads the reviews of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the recorded request has the method "POST" and the address "https://api.github.com/graphql"
    And the content of the recorded request is JSON whose query names "reviews"
    And the variables of the recorded request hold the owner "sbiondolillo", the name "factory-scratch" and the number 30

  Scenario: A review is returned with its author and state
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of reviews with one review with the author login "sbiondolillo", the state "COMMENTED" and the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"
    When a caller reads the reviews of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns one review with the author "sbiondolillo", the state "COMMENTED" and the commit "d7d0afd495cc64aa2ac153de8d001ad8d86fe074"

  Scenario: A pull request with no review gives an empty list of reviews
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of reviews with an empty list of reviews
    When a caller reads the reviews of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns an empty list of reviews

  Scenario: Reading the review threads sends the GraphQL POST with the owner, name and number
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of review threads with an empty list of threads
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the recorded request has the method "POST" and the address "https://api.github.com/graphql"
    And the content of the recorded request is JSON whose query names "reviewThreads"
    And the variables of the recorded request hold the owner "sbiondolillo", the name "factory-scratch" and the number 30

  Scenario: An unresolved thread is returned with its id, path, line and comment
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of review threads with one unresolved thread with the id "PRRT_kwDOUpm1Qc6nMu2z", the path "probe-m8-1790699275.txt", the line 3 and one comment with the author login "sbiondolillo" and the body "This line repeats the line above."
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns one unresolved thread with the id "PRRT_kwDOUpm1Qc6nMu2z", the path "probe-m8-1790699275.txt", the line 3 and one comment with the author "sbiondolillo" and the body "This line repeats the line above."

  Scenario: A thread whose isResolved is true is returned as resolved
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of review threads with a thread whose isResolved is true
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns a resolved thread

  Scenario: The comments keep the order of GitHub's answer
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of review threads with a thread whose comments are first "Name the unit of the timeout." and then "The timeout is in seconds."
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns a thread whose first comment has the body "Name the unit of the timeout." and whose second comment has the body "The timeout is in seconds."

  Scenario: A thread whose line is null reads with a null line
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of review threads with a thread whose line is null
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns a thread whose line is null

  Scenario: A pull request with no review thread gives an empty list of threads
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the read of review threads with an empty list of threads
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads returns an empty list of threads

  Scenario: Replying to a thread sends the GraphQL POST with the thread id and body
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the reply to a thread with success
    When a caller replies to the thread "PRRT_kwDOUpm1Qc6nMu0Y" with the text "The timeout is in seconds."
    Then the recorded request has the method "POST" and the address "https://api.github.com/graphql"
    And the content of the recorded request is JSON whose query names "addPullRequestReviewThreadReply"
    And the variables of the recorded request hold the threadId "PRRT_kwDOUpm1Qc6nMu0Y" and the body "The timeout is in seconds."

  Scenario: Resolving a thread sends the GraphQL POST with the thread id
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the resolve of a thread with success
    When a caller resolves the thread "PRRT_kwDOUpm1Qc6nMu0Y"
    Then the recorded request has the method "POST" and the address "https://api.github.com/graphql"
    And the content of the recorded request is JSON whose query names "resolveReviewThread"
    And the variables of the recorded request hold the threadId "PRRT_kwDOUpm1Qc6nMu0Y"

  Scenario: A GraphQL answer with errors throws GitHubGraphQLException on a read
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the request with the status code 200 and a list errors holding the message "Could not resolve to a PullRequest with the number of 0."
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads throws a GitHubGraphQLException whose message holds "Could not resolve to a PullRequest with the number of 0."

  Scenario: A GraphQL answer with errors throws GitHubGraphQLException on a reply
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the request with the status code 200 and a list errors holding the message "Could not resolve to a node with the global id of 'PRRT_unknown'."
    When a caller replies to the thread "PRRT_unknown" with the text "The timeout is in seconds."
    Then ReviewThreads throws a GitHubGraphQLException whose message holds "Could not resolve to a node with the global id of 'PRRT_unknown'."

  Scenario: A 401 answer throws GitHubRequestException with the status code 401
    Given a ReviewThreads built over a GitHub client
    And GitHub answers the request with the status code 401
    When a caller reads the review threads of pull request 30 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then ReviewThreads throws a GitHubRequestException that gives the status code 401
