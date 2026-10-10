Feature: GitHub client

  @bullet-1
  Scenario: A request goes to the GitHub address through the HttpClient
    Given a GitHub client built with the token "token-1"
    When I send a "GET" request for the path "/repos/sbiondolillo/factory-scratch/branches/main"
    Then the stand-in received one request
    And the recorded request has the method "GET" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/branches/main"

  @bullet-2
  Scenario: A request carries the bearer token
    Given a GitHub client built with the token "token-1"
    When I send a "GET" request for the path "/repos/sbiondolillo/factory-scratch/branches/main"
    Then the recorded request holds the header "Authorization" with the value "Bearer token-1"

  @bullet-3
  Scenario: A request carries the user agent
    Given a GitHub client built with the token "token-1"
    When I send a "GET" request for the path "/repos/sbiondolillo/factory-scratch/branches/main"
    Then the recorded request holds the header "User-Agent" with the value "dotnet-software-factory"

  @bullet-4
  Scenario: A request body is sent as JSON
    Given a GitHub client built with the token "token-1"
    When I send a "POST" request for the path "/repos/sbiondolillo/factory-scratch/issues" with a JSON body whose "title" is "The greeter greets in French"
    Then the content of the recorded request is JSON whose "title" is "The greeter greets in French"

  @bullet-5
  Scenario: The JSON of the answer is returned
    Given a GitHub client built with the token "token-1"
    And GitHub answers with the status code 200 and JSON whose "name" is "main"
    When I send a "GET" request for the path "/repos/sbiondolillo/factory-scratch/branches/main"
    Then the returned JSON has "name" equal to "main"

  @bullet-6
  Scenario: Reading an issue requests the issue address
    Given a GitHub client built with the token "token-1"
    When I read issue 4 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the recorded request has the method "GET" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/issues/4"

  @bullet-7
  Scenario: An issue is read with title, body, author and labels
    Given a GitHub client built with the token "token-1"
    And GitHub answers the read of an issue with the title "The greeter greets in French", the body "The greeter takes a name and a language.", the user login "sbiondolillo" and the labels "bug" and "question"
    When I read issue 4 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the issue has the title "The greeter greets in French", the body "The greeter takes a name and a language." and the author "sbiondolillo"
    And the issue has the labels "bug" and "question"

  @bullet-8
  Scenario: A null body reads as the empty string
    Given a GitHub client built with the token "token-1"
    And GitHub answers the read of an issue with the body null
    When I read issue 5 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the issue has the empty string as its body

  @bullet-9
  Scenario: An empty list of labels is read as an empty list
    Given a GitHub client built with the token "token-1"
    And GitHub answers the read of an issue with an empty list of labels
    When I read issue 5 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the issue has an empty list of labels

  @bullet-10
  Scenario: A comment is posted on an issue
    Given a GitHub client built with the token "token-1"
    When I post the comment "The run is done." on issue 4 of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the recorded request has the method "POST" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/issues/4/comments"
    And the content of the recorded request is JSON whose "body" is "The run is done."

  @bullet-11
  Scenario: A failure status throws an exception naming the request
    Given a GitHub client built with the token "token-1"
    And GitHub answers with a status code outside 200 to 299 and a body
    When I send a "GET" request for the path "/repos/sbiondolillo/factory-scratch/branches/main"
    Then the send fails with a GitHubRequestException
    And the exception message names the method "GET" and the path "/repos/sbiondolillo/factory-scratch/branches/main"
    And the exception message holds the status code and the body of the answer

  @bullet-12
  Scenario: A 404 answer gives the status code 404
    Given a GitHub client built with the token "token-1"
    And GitHub answers with the status code 404 and a body
    When I send a "GET" request for the path "/repos/sbiondolillo/factory-scratch/branches/main"
    Then the send fails with a GitHubRequestException
    And the exception gives the status code 404
