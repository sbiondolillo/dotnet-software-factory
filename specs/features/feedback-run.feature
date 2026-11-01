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

  Scenario: An error flag from claude passes no gate
    Given claude answers the build prompt with the error flag set
    When a caller starts a feedback run
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos

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

  Scenario: The evidence of a red test gate is in the next build prompt
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a feedback run
    Then the second build prompt holds "Assert.Equal() Failure: Values differ"

  Scenario: A red test gate sends the run back before any review
    Given the test gate is red after the first build
    When a caller starts a feedback run
    Then FeedbackRun runs the second build prompt before any prompt with the model "review-model"

  Scenario: A red test gate then a pass spends one Redo
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a feedback run
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 1 Redo

  Scenario: A rejecting review puts its blocking items in the next build prompt
    Given the review rejects the first build and approves the second build
    When a caller starts a feedback run
    Then the second build prompt holds "The total of an empty cart is unset."

  Scenario: A rejecting review then an approval spends one Redo
    Given the review rejects the first build and approves the second build
    When a caller starts a feedback run
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 1 Redo

  Scenario: A red report, a red test gate and a rejecting review spend the three Redos
    Given on the first build prompt the agent writes no report
    And the test gate is red after the second build
    And the review rejects the third build and approves the fourth build
    When a caller starts a feedback run
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 3 Redos

  Scenario: A run has four builds at most
    Given the test gate is red after each of four builds
    When a caller starts a feedback run
    Then FeedbackRun runs 4 build prompts in the run

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

  Scenario: A spent budget after a red gate and rejecting reviews holds the blocking items
    Given the test gate is red after the first build and the third build
    And the review rejects the second build and the fourth build
    When a caller starts a feedback run
    Then FeedbackRun posts one comment on the pull request 10 that holds "The total of an empty cart is unset."
    And FeedbackRun gives back a result with 0 threads resolved, 2 threads unresolved and 3 Redos

  Scenario: A comment over the limit is cut
    Given the test gate is red after each of four builds
    And the test command writes the letter "a" 300000 times to its standard output
    When a caller starts a feedback run
    Then FeedbackRun posts one comment of 262144 bytes or fewer in bytes, and the comment holds the letter "a" 200000 times in sequence and "[cut]"

  Scenario: The container is removed after the last reply
    When a caller starts a feedback run
    Then FeedbackRun removes the container after the last reply

  Scenario: A GraphQL error on a reply throws and removes the container
    Given GitHub answers the reply to the thread "PRRT_kwDOUpm1Qc6ofWu5" with the status code 200 and a list errors
    When a caller starts a feedback run
    Then FeedbackRun throws GitHubGraphQLException
    And FeedbackRun removes the container

  @managed
  Scenario: A managed feedback run moves the head branch to the session's work and pushes it once before the first reply
    Given the run uses Managed Agents sessions
    When a caller starts a feedback run
    Then git checks out "factory/a1b2c3d4", pulls "factory/a1b2c3d4-session" into it and pushes "factory/a1b2c3d4", in this order
    And git pushes the head branch "factory/a1b2c3d4" once, before FeedbackRun adds the first reply

  @managed
  Scenario: A managed feedback run deletes the session branch after the last reply
    Given the run uses Managed Agents sessions
    When a caller starts a feedback run
    Then git deletes the branch "factory/a1b2c3d4-session" on origin as its last git command

  @managed
  Scenario: A managed feedback run with a spent budget pushes no head branch
    Given the run uses Managed Agents sessions
    And the test gate is red after each of four builds
    When a caller starts a feedback run
    Then git pushes no branch "factory/a1b2c3d4"
    And git deletes the branch "factory/a1b2c3d4-session" on origin as its last git command
    And FeedbackRun adds no reply

  @managed
  Scenario: A session commit of a file under .factory/ stops a managed feedback run with a comment on the pull request before any thread is resolved
    Given the run uses Managed Agents sessions
    And the session branch tracks the file ".factory/threads.json"
    When a caller starts a feedback run
    Then FeedbackRun posts one comment on the pull request 10 that holds "The session committed files under .factory/, which only the factory writes: .factory/threads.json. The run stops."
    And FeedbackRun throws SessionBranchException with a message that holds "The session committed files under .factory/, which only the factory writes: .factory/threads.json. The run stops."
    And FeedbackRun adds no reply
    And FeedbackRun resolves no thread

  @managed
  Scenario: A managed feedback run runs the gates in a container with no key and builds on the session branch of the head branch
    Given the run uses Managed Agents sessions
    When a caller starts a feedback run
    Then the docker run of the run holds neither "ANTHROPIC_API_KEY" nor "ANTHROPIC_BASE_URL"
    And the docker run of the run mounts the clone at /work and names the image "factory-sandbox:test"
    And every dotnet command of the run goes through docker exec
    And git pushes the session branch "factory/a1b2c3d4-session" before the first session starts
    And FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos
    And FeedbackRun removes the container

  @managed
  Scenario: A file under .factory/ on the head branch stops a managed feedback run before the first session starts
    Given the run uses Managed Agents sessions
    And the session branch tracks the file ".factory/leak.txt"
    When a caller starts a feedback run
    Then FeedbackRun throws SessionBranchException with a message that holds "The session committed files under .factory/, which only the factory writes: .factory/leak.txt. The run stops."
    And the run starts no session

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
