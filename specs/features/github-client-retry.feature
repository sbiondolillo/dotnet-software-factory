Feature: GitHubClient sends a request again when GitHub fails for a moment

  @bullet-1
  Scenario: A 500 then a 502 then a 200 gives back the JSON after 3 requests
    Given a GitHub client with a base wait of zero
    And GitHub answers with the statuses 500, 502 and 200 with a JSON body
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync gives back the JSON body of the successful answer
    And GitHub received 3 requests

  @bullet-2
  Scenario: A 503 on every request throws after 4 requests
    Given a GitHub client with a base wait of zero
    And GitHub answers every request with the status 503
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync throws GitHubRequestException whose status code is 503
    And GitHub received 4 requests

  @bullet-3
  Scenario: A 429 then a 200 gives back the JSON after 2 requests
    Given a GitHub client with a base wait of zero
    And GitHub answers with the statuses 429 and 200 with a JSON body
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync gives back the JSON body of the successful answer
    And GitHub received 2 requests

  @bullet-4
  Scenario: A 403 with a Retry-After of 0 seconds then a 200 gives back the JSON after 2 requests
    Given a GitHub client with a base wait of zero
    And GitHub answers with a 403 that holds a Retry-After header of 0 seconds and then a 200 with a JSON body
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync gives back the JSON body of the successful answer
    And GitHub received 2 requests

  @bullet-5
  Scenario: A 403 with no Retry-After header throws at once
    Given a GitHub client with a base wait of zero
    And GitHub answers every request with a 403 that holds no Retry-After header
    When I send a "GET" request for the path "/repos/o/r/issues/1"
    Then SendAsync throws GitHubRequestException whose status code is 403
    And GitHub received 1 request

  @bullet-6
  Scenario: A POST to pulls answered with 500 then 201 gives back the JSON after 2 requests
    Given a GitHub client with a base wait of zero
    And GitHub answers with the statuses 500 and 201 with a JSON body
    When I send a "POST" request for the path "/repos/o/r/pulls" with a JSON body whose "title" is "t"
    Then SendAsync gives back the JSON body of the successful answer
    And GitHub received 2 requests

  @bullet-7
  Scenario: A PUT to contents answered with 409 then 201 gives back the JSON after 2 requests
    Given a GitHub client with a base wait of zero
    And GitHub answers with the statuses 409 and 201 with a JSON body
    When I send a "PUT" request for the path "/repos/o/r/contents/a.txt" with a JSON body whose "message" is "m"
    Then SendAsync gives back the JSON body of the successful answer
    And GitHub received 2 requests

  @bullet-8
  Scenario: A POST to pulls answered with 422 throws at once
    Given a GitHub client with a base wait of zero
    And GitHub answers every request with the status 422
    When I send a "POST" request for the path "/repos/o/r/pulls" with a JSON body whose "title" is "t"
    Then SendAsync throws GitHubRequestException whose status code is 422
    And GitHub received 1 request

  @bullet-9
  Scenario: A request sent again carries the same method, path, token, user agent and body
    Given a GitHub client with a base wait of zero and the token "token-9"
    And GitHub answers with the statuses 500 and 201 with a JSON body
    When I send a "POST" request for the path "/repos/o/r/pulls" with a JSON body whose "title" is "t"
    Then the second recorded request has the same method, path, bearer token, user agent and JSON body as the first
