Feature: Pull requests

  Scenario: Opening a pull request sends the POST with the head, base, title and body
    Given a PullRequests built over a GitHub client
    And no pull request from the branch "factory/run-1" is open
    And GitHub answers the open of a pull request with the number 10 and the html_url "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller opens a pull request on the repo "factory-scratch" of the owner "sbiondolillo" from the branch "factory/run-1" into "main" with the title "The greeter greets in French" and the body "The run is done."
    Then the recorded open request has the method "POST" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/pulls"
    And the content of the recorded open request is JSON whose head is "factory/run-1", whose base is "main", whose title is "The greeter greets in French" and whose body is "The run is done."

  Scenario: GitHub's answer gives the number and web address
    Given a PullRequests built over a GitHub client
    And no pull request from the branch "factory/run-1" is open
    And GitHub answers the open of a pull request with the number 10 and the html_url "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When a caller opens a pull request on the repo "factory-scratch" of the owner "sbiondolillo" from the branch "factory/run-1" into "main" with the title "The greeter greets in French" and the body "The run is done."
    Then PullRequests returns a pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  Scenario: A pull request already open from the branch is returned
    Given a PullRequests built over a GitHub client
    And the pull request 10, with the html_url "https://github.com/sbiondolillo/factory-scratch/pull/10", is already open from the branch "factory/run-1"
    When a caller opens a pull request on the repo "factory-scratch" of the owner "sbiondolillo" from the branch "factory/run-1" into "main" with the title "The greeter greets in French" and the body "The run is done."
    Then PullRequests returns a pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  Scenario: Reading check runs sends the GET for the commit
    Given a PullRequests built over a GitHub client
    And GitHub answers the read of check runs with no check runs
    When a caller reads the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then the recorded read request has the method "GET" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/commits/4e24cdc9f280411706bb0de5fabcbb06e6626348/check-runs"

  Scenario: A completed check run is returned with its name, status and conclusion
    Given a PullRequests built over a GitHub client
    And GitHub answers the read of check runs with one check run with the name "fixture", the status "completed" and the conclusion "success"
    When a caller reads the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns one check run with the name "fixture", the status "completed" and the conclusion "success"

  Scenario: A check run that is not complete has the empty string as its conclusion
    Given a PullRequests built over a GitHub client
    And GitHub answers the read of check runs with one check run with the name "build", the status "in_progress" and the conclusion null
    When a caller reads the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns one check run with the name "build", the status "in_progress" and the empty string as its conclusion

  Scenario: A commit with no check run gives an empty list
    Given a PullRequests built over a GitHub client
    And GitHub answers the read of check runs with no check runs
    When a caller reads the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns an empty list of check runs

  Scenario: A 422 answer to the read of check runs throws GitHubRequestException
    Given a PullRequests built over a GitHub client
    And GitHub answers the read of check runs with the status code 422
    When a caller reads the check runs on the commit "0000000000000000000000000000000000000000" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests throws a GitHubRequestException that gives the status code 422
