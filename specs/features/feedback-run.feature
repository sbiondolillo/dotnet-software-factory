Feature: A feedback run answers every unresolved review thread

  Background:
    Given the repo "factory-scratch" of the owner "sbiondolillo", the pull request 10, and an empty list of secret names
    And the build uses the model "build-model" with the turn limit 60 and the review uses the model "review-model" with the turn limit 30
    And the author of each commit is "Factory" with the email "factory@example.com"
    And GitHub answers the read of the review threads of the pull request with the threads A and B
    And GitHub answers the read of the pull request with the head ref "factory/a1b2c3d4"
    And git answers each command with the exit code 0
    And the head commit of the clone after the checkout is "c7d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293"
    And git reports a change in the work tree after each build
    And on a build prompt the agent writes the report that marks A and B fixed
    And claude answers each prompt with the error flag clear
    And each dotnet command exits with the code 0 and the TRX file holds one passed test for each scenario
    And on a review prompt the agent writes an approving review
    And GitHub answers each reply and each resolve with the status code 200 and no errors

  Scenario: A pull request with only a resolved thread ends the run before any clone
    Given GitHub answers the read of the review threads of the pull request with the thread C alone
    When a caller starts a feedback run
    Then FeedbackRun clones no repo and starts no container
    And FeedbackRun gives back a result with 0 threads resolved, 0 threads unresolved and 0 Redos

  Scenario: The run clones the repo and checks out the head branch
    When a caller starts a feedback run
    Then git clones "https://github.com/sbiondolillo/factory-scratch.git" into the directory that the caller gave and checks out the branch "factory/a1b2c3d4" in it

  Scenario: The build prompt holds every thread
    When a caller starts a feedback run
    Then FeedbackRun runs one prompt with the model "build-model" and the turn limit 60
    And that prompt holds "PRRT_kwDOUpm1Qc6ofWu5", "src/Core/Checkout.cs", "14", "sbiondolillo", "The total ignores the second item of the cart.", "PRRT_kwDOUpm1Qc6ofWvu", "tests/Specs/Unit/CheckoutTests.cs", "9" and "Name the unit of the total."

  Scenario: The build prompt names the report and its statuses
    When a caller starts a feedback run
    Then the build prompt holds ".factory/threads.json", "fixed" and "disputed"

  Scenario: A resolved thread gets no reply
    Given GitHub answers the read of the review threads of the pull request with the threads A, B and C
    When a caller starts a feedback run
    Then FeedbackRun adds a reply to the thread A and a reply to the thread B
    And FeedbackRun adds 2 replies in the run

  Scenario: The first prompt of the run is the build prompt
    When a caller starts a feedback run
    Then the first prompt of the run has the model "build-model"

  Scenario: The review prompt holds the base commit
    When a caller starts a feedback run
    Then FeedbackRun runs a review prompt that holds "c7d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293"

  Scenario: The review prompt follows a passing test gate
    When a caller starts a feedback run
    Then FeedbackRun runs one prompt with the model "review-model" and the turn limit 30
    And that prompt holds "The total ignores the second item of the cart."

  Scenario: The commit carries the title of the run and the author
    When a caller starts a feedback run
    Then git commits the change of the build with the message "Answer the review threads of pull request #10", the author name "Factory" and the author email "factory@example.com"

  Scenario: The secret names reach the test command
    Given the caller gives the secret names "WEATHER_API_KEY" and "MAPS_API_KEY"
    When a caller starts a feedback run
    Then docker gives both names "WEATHER_API_KEY" and "MAPS_API_KEY" to the command "dotnet run --no-restore --project tests/Integration"

  Scenario: An approved run pushes the branch
    When a caller starts a feedback run
    Then git pushes the branch "factory/a1b2c3d4"

  Scenario: The push comes before the first reply
    When a caller starts a feedback run
    Then git pushes the branch "factory/a1b2c3d4" before FeedbackRun adds the first reply

  Scenario: A fixed thread gets its reply and is resolved
    When a caller starts a feedback run
    Then FeedbackRun adds the reply "The total now adds every item of the cart." to the thread "PRRT_kwDOUpm1Qc6ofWu5" and resolves the thread "PRRT_kwDOUpm1Qc6ofWu5"

  Scenario: An approved run resolves both threads
    When a caller starts a feedback run
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos

  Scenario: A round appends its own section to the job summary
    When a caller starts a feedback run
    Then the job summary of the round is one section "## Feedback run" with the rows "build 1", "lint 1", "test 1" and "review 1", in this order

  Scenario: A missing report sends the run back to the build before any gate
    Given on the first build prompt the agent writes no report
    When a caller starts a feedback run
    Then FeedbackRun runs the second build prompt before any dotnet command

  Scenario: A missing report is a fault that spends one Redo
    Given on the first build prompt the agent writes no report
    When a caller starts a feedback run
    Then the second build prompt holds ".factory/threads.json"
    And FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 1 Redo

  Scenario: A report that is no JSON object with threads is a fault
    Given on the first build prompt the agent writes the text "The threads are fixed." to the report
    When a caller starts a feedback run
    Then the second build prompt holds ".factory/threads.json"
    And FeedbackRun gives back a result with 1 Redo

  Scenario: A thread with no item is a fault
    Given on the first build prompt the agent writes a report with an item for the thread A alone
    When a caller starts a feedback run
    Then the second build prompt holds "PRRT_kwDOUpm1Qc6ofWvu"
    And FeedbackRun gives back a result with 1 Redo

  Scenario: A status other than fixed or disputed is a fault
    Given on the first build prompt the agent writes a report that gives the thread A the status "done"
    When a caller starts a feedback run
    Then the second build prompt holds "PRRT_kwDOUpm1Qc6ofWu5" and "done"
    And FeedbackRun gives back a result with 1 Redo

  Scenario: A blank reply is a fault
    Given on the first build prompt the agent writes a report that gives the thread A a reply of two spaces
    When a caller starts a feedback run
    Then the second build prompt holds "PRRT_kwDOUpm1Qc6ofWu5" and "reply"
    And FeedbackRun gives back a result with 1 Redo

  Scenario: A report fault after a red test gate sends the run back again
    Given on the first build prompt the agent writes the report and on the second build prompt the agent writes no file
    And the test gate is red after the first build
    When a caller starts a feedback run
    Then the third build prompt holds ".factory/threads.json"

  Scenario: A spent budget posts one comment with the evidence
    Given the test gate is red after each of four builds
    When a caller starts a feedback run
    Then FeedbackRun posts one comment on the pull request 10 of "sbiondolillo/factory-scratch" and the comment holds "Assert.Equal() Failure: Values differ"

  Scenario: A spent budget pushes nothing and replies to no thread
    Given the test gate is red after each of four builds
    When a caller starts a feedback run
    Then git pushes no branch
    And FeedbackRun adds no reply
    And FeedbackRun gives back a result with 0 threads resolved, 2 threads unresolved and 3 Redos

  Scenario: The container is removed after the last reply
    When a caller starts a feedback run
    Then FeedbackRun removes the container after the last reply

  Scenario: A GraphQL error on a reply throws and removes the container
    Given GitHub answers the reply to the thread "PRRT_kwDOUpm1Qc6ofWu5" with the status code 200 and a list errors
    When a caller starts a feedback run
    Then FeedbackRun throws GitHubGraphQLException
    And FeedbackRun removes the container

  Scenario: The feedback run collects the work of the agents onto the head branch before it pushes it
    When a caller starts a feedback run
    Then the backend collects onto "factory/a1b2c3d4" before git pushes "factory/a1b2c3d4"
    And git pushes the head branch "factory/a1b2c3d4" once, before FeedbackRun adds the first reply

  Scenario: The feedback run hands the backend the session branch of the head branch, the head commit and its title
    When a caller starts a feedback run
    Then the run hands the backend the session branch "factory/a1b2c3d4-session", the base commit "c7d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293" and the title "Answer the review threads of pull request #10"

  Scenario: The job summary of a feedback run ends with the sessions of the backend
    Given the backend has the sessions "sesn_01" of the builder at 300 cents and "sesn_02" of the reviewer at 37 cents
    When a caller starts a feedback run
    Then the job summary of the run ends with the lines
      """
      ### Sessions

      - sesn_01, builder, $3.00
      - sesn_02, reviewer, $0.37
      """

  Scenario: A stop of the session branch posts its text on the pull request and resolves no thread
    Given the builder stops the run on the first build with the text "The session committed files under .factory/, which only the factory writes: .factory/threads.json. The run stops."
    When a caller starts a feedback run
    Then FeedbackRun posts one comment on the pull request 10 that holds "The session committed files under .factory/, which only the factory writes: .factory/threads.json. The run stops."
    And FeedbackRun gives back a result with 0 threads resolved, 2 threads unresolved and 0 Redos
    And FeedbackRun adds no reply
    And FeedbackRun resolves no thread

  Scenario: A disputed thread gets its reply and is resolved
    Given on a build prompt the agent writes the report that marks A fixed and B disputed
    When a caller starts a feedback run
    Then FeedbackRun adds the reply "The title of the test names the unit." to the thread "PRRT_kwDOUpm1Qc6ofWvu" and resolves the thread "PRRT_kwDOUpm1Qc6ofWvu"

  Scenario: A run with one disputed thread resolves both threads
    Given on a build prompt the agent writes the report that marks A fixed and B disputed
    When a caller starts a feedback run
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos
    And FeedbackRun resolves the threads "PRRT_kwDOUpm1Qc6ofWu5" and "PRRT_kwDOUpm1Qc6ofWvu"

  Scenario: A run with every thread disputed and no change still pushes and resolves both threads
    Given on a build prompt the agent writes the report that marks A and B disputed
    And git reports no change in the work tree after the build
    When a caller starts a feedback run
    Then git pushes the branch "factory/a1b2c3d4"
    And FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos
    And FeedbackRun resolves the threads "PRRT_kwDOUpm1Qc6ofWu5" and "PRRT_kwDOUpm1Qc6ofWvu"
