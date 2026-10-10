Feature: GitHubClient sends a request again when the request times out

  Scenario: A request that gets no answer then a 200 gives back the JSON after 2 requests
    Given a GitHub client with a base wait of zero and a timeout of 1 second for one request
    And GitHub gives no answer to the first request and answers the second with 200 and a JSON body
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync gives back the JSON body of the successful answer
    And GitHub received 2 requests

  Scenario: A request that gets no answer every time throws a TimeoutException after 4 requests
    Given a GitHub client with a base wait of zero and a timeout of 1 second for one request
    And GitHub gives no answer to any request
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync throws a TimeoutException whose message holds "GET" and "/repos/o/r/issues/1"
    And GitHub received 4 requests

  Scenario: A cancel during the second request throws an OperationCanceledException
    Given a GitHub client with a base wait of zero and a timeout of 1 second for one request
    And GitHub gives no answer to any request
    When I send a "GET" request for the path "/repos/o/r/issues/1" and the caller cancels its token during the second request
    Then SendAsync throws an OperationCanceledException
    And GitHub received 2 requests

  Scenario: A client built with no timeout reports a timeout of 30 seconds
    Given a GitHub client built with no timeout
    Then the client reports a timeout of 30 seconds for one request
