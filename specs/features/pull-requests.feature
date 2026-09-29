Feature: Pull requests and check runs

  @bullet-1
  Scenario: Opening a pull request sends the branches, title and body
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And no pull request from the branch "factory/run-1" is open
    When I open a pull request on the repo "factory-scratch" of the owner "sbiondolillo" from the branch "factory/run-1" into "main" with the title "The greeter greets in French" and the body "The run is done."
    # run: printf 'POST\nhttps://api.github.com/repos/sbiondolillo/factory-scratch/pulls'
    Then the recorded open request has the method "POST" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/pulls"
    # run: printf 'head\nfactory/run-1\nbase\nmain\ntitle\nThe greeter greets in French\nbody\nThe run is done.'
    And the content of the recorded open request is JSON whose "head" is "factory/run-1", whose "base" is "main", whose "title" is "The greeter greets in French" and whose "body" is "The run is done."

  @bullet-2
  Scenario: The opened pull request is returned with its number and web address
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And no pull request from the branch "factory/run-1" is open
    And GitHub answers the open of a pull request with the number 10 and the html_url "https://github.com/sbiondolillo/factory-scratch/pull/10"
    When I open a pull request on the repo "factory-scratch" of the owner "sbiondolillo" from the branch "factory/run-1" into "main" with the title "The greeter greets in French" and the body "The run is done."
    # run: printf '10\nhttps://github.com/sbiondolillo/factory-scratch/pull/10'
    Then the returned pull request has the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-3
  Scenario: A pull request already open from the branch is returned
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And the pull request 10 with the html_url "https://github.com/sbiondolillo/factory-scratch/pull/10" is already open from the branch "factory/run-1"
    When I open a pull request on the repo "factory-scratch" of the owner "sbiondolillo" from the branch "factory/run-1" into "main" with the title "The greeter greets in French" and the body "The run is done."
    # run: printf '10\nhttps://github.com/sbiondolillo/factory-scratch/pull/10'
    Then the returned pull request has the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  @bullet-4
  Scenario: Reading check runs requests the check-runs address of the commit
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    When I read the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    # run: printf 'GET\nhttps://api.github.com/repos/sbiondolillo/factory-scratch/commits/4e24cdc9f280411706bb0de5fabcbb06e6626348/check-runs'
    Then the recorded check-runs request has the method "GET" and the address "https://api.github.com/repos/sbiondolillo/factory-scratch/commits/4e24cdc9f280411706bb0de5fabcbb06e6626348/check-runs"

  @bullet-5
  Scenario: A completed check run is returned with name, status and conclusion
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And GitHub answers the read of check runs with one check run with the name "fixture", the status "completed" and the conclusion "success"
    When I read the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns one check run
    # run: printf 'fixture\ncompleted\nsuccess'
    And the check run has the name "fixture", the status "completed" and the conclusion "success"

  @bullet-6
  Scenario: A check run that is not complete has the empty string as its conclusion
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And GitHub answers the read of check runs with one check run with the name "build", the status "in_progress" and a null conclusion
    When I read the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns one check run
    # run: printf 'build\nin_progress'
    And the check run has the name "build" and the status "in_progress"
    And the check run has the empty string as its conclusion

  @bullet-7
  Scenario: A commit with no check run reads as an empty list
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And GitHub answers the read of check runs with the total_count 0 and an empty list of check runs
    When I read the check runs on the commit "4e24cdc9f280411706bb0de5fabcbb06e6626348" of the repo "factory-scratch" of the owner "sbiondolillo"
    Then PullRequests returns an empty list of check runs

  @bullet-8
  Scenario: A failed read of check runs throws the GitHub request exception
    Given a PullRequests built over a GitHubClient with a stand-in HttpClient
    And GitHub answers the read of check runs with the status code 422
    When I read the check runs on the commit "0000000000000000000000000000000000000000" of the repo "factory-scratch" of the owner "sbiondolillo"
    # run: printf '422'
    Then PullRequests throws a GitHubRequestException that gives the status code 422
