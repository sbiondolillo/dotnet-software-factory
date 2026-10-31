Feature: The GitHub App mints and revokes an installation token

  Background:
    Given a GitHubAppTokens with the app id "123456" and the installation id "7890", a new test RSA key and the time "2026-10-30T12:00:00Z", over a stub HttpMessageHandler

  Scenario: The mint posts to the access tokens path of the installation
    Given GitHub answers the mint with the status 201 and the token "ghs_test1"
    When a caller mints a token for "dotnet-software-factory" with write access
    Then the stub recorded one request "POST https://api.github.com/app/installations/7890/access_tokens"
    And the request holds the header "Accept" set to "application/vnd.github+json"
    And the request holds the header "User-Agent" set to "dotnet-software-factory"

  Scenario: The mint authorizes with a Bearer JWT that RS256 signs, whose issuer is the app id and whose expiry is within 10 minutes
    Given GitHub answers the mint with the status 201 and the token "ghs_test2"
    When a caller mints a token for "dotnet-software-factory" with write access
    Then the request authorizes with "Bearer" and a JWT whose header is {"alg":"RS256","typ":"JWT"}
    And the signature of the JWT verifies with the public key of the test RSA key
    And the JWT's "iss" is "123456"
    And the JWT's "iat" is 60 seconds before the time and its "exp" is 540 seconds after the time

  Scenario: A mint with write access asks for contents write on the one repo
    Given GitHub answers the mint with the status 201 and the token "ghs_test3"
    When a caller mints a token for "dotnet-software-factory" with write access
    Then the request body is {"repositories":["dotnet-software-factory"],"permissions":{"contents":"write","metadata":"read"}}

  Scenario: A mint with read access asks for contents read on the one repo
    Given GitHub answers the mint with the status 201 and the token "ghs_test4"
    When a caller mints a token for "factory-scratch" with read access
    Then the request body is {"repositories":["factory-scratch"],"permissions":{"contents":"read","metadata":"read"}}

  Scenario: The mint gives back the token of the answer
    Given GitHub answers the mint with the status 201 and the token "ghs_test5"
    When a caller mints a token for "dotnet-software-factory" with write access
    Then the minted token is "ghs_test5"

  Scenario: A mint that GitHub answers with another status throws GitHubRequestException with the status and the body
    Given GitHub answers with the status 401 and the body "Bad credentials"
    When a caller mints a token for "dotnet-software-factory" with write access
    Then the call throws GitHubRequestException whose message holds "401" and "Bad credentials"

  Scenario: The revoke sends DELETE /installation/token with the token as the Bearer
    Given GitHub answers with the status 204 and the body ""
    When a caller revokes the token "ghs_old1"
    Then the stub recorded one request "DELETE https://api.github.com/installation/token"
    And the request holds the header "Authorization" set to "Bearer ghs_old1"
    And the request holds the header "User-Agent" set to "dotnet-software-factory"

  Scenario: A revoke that GitHub answers with another status throws GitHubRequestException without the token
    Given GitHub answers with the status 401 and the body "Bad credentials"
    When a caller revokes the token "ghs_old2"
    Then the call throws GitHubRequestException whose message holds "401" and "Bad credentials"
    And the message of the exception lacks "ghs_old2"
