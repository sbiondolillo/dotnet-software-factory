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

  @bullet-3
  Scenario: A pull request with only a resolved thread ends the run before any clone
    Given GitHub answers the read of the review threads of the pull request with the thread C alone
    When a caller starts a feedback run
    Then FeedbackRun clones no repo and starts no container
    # run: printf '0\n0\n0'
    And FeedbackRun gives back a result with 0 threads resolved, 0 threads unresolved and 0 Redos

  @bullet-4
  Scenario: The run clones the repo and checks out the head branch
    When a caller starts a feedback run
    # run: printf 'https://github.com/sbiondolillo/factory-scratch.git\nfactory/a1b2c3d4'
    Then git clones "https://github.com/sbiondolillo/factory-scratch.git" into the directory that the caller gave and checks out the branch "factory/a1b2c3d4" in it

  @bullet-5
  Scenario: The build prompt holds every thread
    When a caller starts a feedback run
    # run: printf 'build-model\n60'
    Then FeedbackRun runs one prompt with the model "build-model" and the turn limit 60
    # run: printf 'PRRT_kwDOUpm1Qc6ofWu5\nsrc/Core/Checkout.cs\n14\nsbiondolillo\nThe total ignores the second item of the cart.\nPRRT_kwDOUpm1Qc6ofWvu\ntests/Specs/Unit/CheckoutTests.cs\n9\nName the unit of the total.'
    And that prompt holds "PRRT_kwDOUpm1Qc6ofWu5", "src/Core/Checkout.cs", "14", "sbiondolillo", "The total ignores the second item of the cart.", "PRRT_kwDOUpm1Qc6ofWvu", "tests/Specs/Unit/CheckoutTests.cs", "9" and "Name the unit of the total."

  @bullet-6
  Scenario: The build prompt names the report and its statuses
    When a caller starts a feedback run
    # run: printf '.factory/threads.json\nfixed\ndisputed'
    Then the build prompt holds ".factory/threads.json", "fixed" and "disputed"

  @bullet-7
  Scenario: A resolved thread gets no reply
    Given GitHub answers the read of the review threads of the pull request with the threads A, B and C
    When a caller starts a feedback run
    Then FeedbackRun adds a reply to the thread A and a reply to the thread B
    # run: printf '2'
    And FeedbackRun adds 2 replies in the run

  @bullet-8
  Scenario: The first prompt of the run is the build prompt
    When a caller starts a feedback run
    # run: printf 'build-model'
    Then the first prompt of the run has the model "build-model"

  @bullet-9
  Scenario: The review prompt holds the base commit
    When a caller starts a feedback run
    # run: printf 'c7d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293'
    Then FeedbackRun runs a review prompt that holds "c7d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293"

  @bullet-10
  Scenario: The review prompt follows a passing test gate
    When a caller starts a feedback run
    # run: printf 'review-model\n30'
    Then FeedbackRun runs one prompt with the model "review-model" and the turn limit 30
    # run: printf 'The total ignores the second item of the cart.'
    And that prompt holds "The total ignores the second item of the cart."

  @bullet-11
  Scenario: The commit carries the title of the run and the author
    When a caller starts a feedback run
    # run: printf 'Answer the review threads of pull request #10\nFactory\nfactory@example.com'
    Then git commits the change of the build with the message "Answer the review threads of pull request #10", the author name "Factory" and the author email "factory@example.com"

  @bullet-12
  Scenario: The secret names reach the test command
    Given the caller gives the secret names "WEATHER_API_KEY" and "MAPS_API_KEY"
    When a caller starts a feedback run
    # run: printf 'WEATHER_API_KEY\nMAPS_API_KEY\ndotnet run --no-restore --project tests/Integration'
    Then docker gives both names "WEATHER_API_KEY" and "MAPS_API_KEY" to the command "dotnet run --no-restore --project tests/Integration"

  @bullet-13
  Scenario: An approved run pushes the branch
    When a caller starts a feedback run
    # run: printf 'factory/a1b2c3d4'
    Then git pushes the branch "factory/a1b2c3d4"

  @bullet-14
  Scenario: The push comes before the first reply
    When a caller starts a feedback run
    # run: printf 'factory/a1b2c3d4'
    Then git pushes the branch "factory/a1b2c3d4" before FeedbackRun adds the first reply

  @bullet-15
  Scenario: A fixed thread gets its reply and is resolved
    When a caller starts a feedback run
    # run: printf 'The total now adds every item of the cart.\nPRRT_kwDOUpm1Qc6ofWu5\nPRRT_kwDOUpm1Qc6ofWu5'
    Then FeedbackRun adds the reply "The total now adds every item of the cart." to the thread "PRRT_kwDOUpm1Qc6ofWu5" and resolves the thread "PRRT_kwDOUpm1Qc6ofWu5"

  @bullet-16
  Scenario: An approved run resolves both threads
    When a caller starts a feedback run
    # run: printf '2\n0\n0'
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos

  @bullet-17
  Scenario: A disputed thread gets its reply and stays unresolved
    Given on a build prompt the agent writes the report that marks A fixed and B disputed
    When a caller starts a feedback run
    # run: printf 'The title of the test names the unit.\nPRRT_kwDOUpm1Qc6ofWvu\nPRRT_kwDOUpm1Qc6ofWu5'
    Then FeedbackRun adds the reply "The title of the test names the unit." to the thread "PRRT_kwDOUpm1Qc6ofWvu", and resolves the thread "PRRT_kwDOUpm1Qc6ofWu5" alone

  @bullet-18
  Scenario: A run with one disputed thread leaves one unresolved
    Given on a build prompt the agent writes the report that marks A fixed and B disputed
    When a caller starts a feedback run
    # run: printf '1\n1\n0'
    Then FeedbackRun gives back a result with 1 thread resolved, 1 thread unresolved and 0 Redos

  @bullet-19
  Scenario: A run with every thread disputed and no change still pushes and resolves nothing
    Given on a build prompt the agent writes the report that marks A and B disputed
    And git reports no change in the work tree after the build
    When a caller starts a feedback run
    # run: printf 'factory/a1b2c3d4'
    Then git pushes the branch "factory/a1b2c3d4"
    # run: printf '0\n2\n0'
    And FeedbackRun resolves no thread and gives back a result with 0 threads resolved, 2 threads unresolved and 0 Redos

  @bullet-20
  Scenario: An error flag from claude passes no gate
    Given claude answers the build prompt with the error flag set
    When a caller starts a feedback run
    # run: printf '2\n0\n0'
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 0 Redos

  @bullet-21
  Scenario: A missing report sends the run back to the build before any gate
    Given on the first build prompt the agent writes no report
    When a caller starts a feedback run
    Then FeedbackRun runs the second build prompt before any dotnet command

  @bullet-22
  Scenario: A missing report is a fault that spends one Redo
    Given on the first build prompt the agent writes no report
    When a caller starts a feedback run
    # run: printf '.factory/threads.json'
    Then the second build prompt holds ".factory/threads.json"
    # run: printf '2\n0\n1'
    And FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 1 Redo

  @bullet-23
  Scenario: A report that is no JSON object with threads is a fault
    Given on the first build prompt the agent writes the text "The threads are fixed." to the report
    When a caller starts a feedback run
    # run: printf '.factory/threads.json'
    Then the second build prompt holds ".factory/threads.json"
    # run: printf '1'
    And FeedbackRun gives back a result with 1 Redo

  @bullet-24
  Scenario: A thread with no item is a fault
    Given on the first build prompt the agent writes a report with an item for the thread A alone
    When a caller starts a feedback run
    # run: printf 'PRRT_kwDOUpm1Qc6ofWvu'
    Then the second build prompt holds "PRRT_kwDOUpm1Qc6ofWvu"
    # run: printf '1'
    And FeedbackRun gives back a result with 1 Redo

  @bullet-25
  Scenario: A status other than fixed or disputed is a fault
    Given on the first build prompt the agent writes a report that gives the thread A the status "done"
    When a caller starts a feedback run
    # run: printf 'PRRT_kwDOUpm1Qc6ofWu5\ndone'
    Then the second build prompt holds "PRRT_kwDOUpm1Qc6ofWu5" and "done"
    # run: printf '1'
    And FeedbackRun gives back a result with 1 Redo

  @bullet-26
  Scenario: A blank reply is a fault
    Given on the first build prompt the agent writes a report that gives the thread A a reply of two spaces
    When a caller starts a feedback run
    # run: printf 'PRRT_kwDOUpm1Qc6ofWu5\nreply'
    Then the second build prompt holds "PRRT_kwDOUpm1Qc6ofWu5" and "reply"
    # run: printf '1'
    And FeedbackRun gives back a result with 1 Redo

  @bullet-27
  Scenario: A report fault after a red test gate sends the run back again
    Given on the first build prompt the agent writes the report and on the second build prompt the agent writes no file
    And the test gate is red after the first build
    When a caller starts a feedback run
    # run: printf '.factory/threads.json'
    Then the third build prompt holds ".factory/threads.json"

  @bullet-28
  Scenario: The evidence of a red test gate is in the next build prompt
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a feedback run
    # run: printf 'Assert.Equal() Failure: Values differ'
    Then the second build prompt holds "Assert.Equal() Failure: Values differ"

  @bullet-29
  Scenario: A red test gate sends the run back before any review
    Given the test gate is red after the first build
    When a caller starts a feedback run
    # run: printf 'review-model'
    Then FeedbackRun runs the second build prompt before any prompt with the model "review-model"

  @bullet-30
  Scenario: A red test gate then a pass spends one Redo
    Given the test gate is red after the first build and passes after the second build
    When a caller starts a feedback run
    # run: printf '2\n0\n1'
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 1 Redo

  @bullet-31
  Scenario: A rejecting review puts its blocking items in the next build prompt
    Given the review rejects the first build and approves the second build
    When a caller starts a feedback run
    # run: printf 'The total of an empty cart is unset.'
    Then the second build prompt holds "The total of an empty cart is unset."

  @bullet-32
  Scenario: A rejecting review then an approval spends one Redo
    Given the review rejects the first build and approves the second build
    When a caller starts a feedback run
    # run: printf '2\n0\n1'
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 1 Redo

  @bullet-33
  Scenario: A red report, a red test gate and a rejecting review spend the three Redos
    Given on the first build prompt the agent writes no report
    And the test gate is red after the second build
    And the review rejects the third build and approves the fourth build
    When a caller starts a feedback run
    # run: printf '2\n0\n3'
    Then FeedbackRun gives back a result with 2 threads resolved, 0 threads unresolved and 3 Redos

  @bullet-34
  Scenario: A run has four builds at most
    Given the test gate is red after each of four builds
    When a caller starts a feedback run
    # run: printf '4'
    Then FeedbackRun runs 4 build prompts in the run

  @bullet-35
  Scenario: A spent budget posts one comment with the evidence
    Given the test gate is red after each of four builds
    When a caller starts a feedback run
    # run: printf '10\nsbiondolillo/factory-scratch\nAssert.Equal() Failure: Values differ'
    Then FeedbackRun posts one comment on the pull request 10 of "sbiondolillo/factory-scratch" and the comment holds "Assert.Equal() Failure: Values differ"

  @bullet-36
  Scenario: A spent budget pushes nothing and replies to no thread
    Given the test gate is red after each of four builds
    When a caller starts a feedback run
    Then git pushes no branch
    And FeedbackRun adds no reply
    # run: printf '0\n2\n3'
    And FeedbackRun gives back a result with 0 threads resolved, 2 threads unresolved and 3 Redos

  @bullet-37
  Scenario: A spent budget after a red gate and rejecting reviews holds the blocking items
    Given the test gate is red after the first build and the third build
    And the review rejects the second build and the fourth build
    When a caller starts a feedback run
    # run: printf '10\nThe total of an empty cart is unset.'
    Then FeedbackRun posts one comment on the pull request 10 that holds "The total of an empty cart is unset."
    # run: printf '0\n2\n3'
    And FeedbackRun gives back a result with 0 threads resolved, 2 threads unresolved and 3 Redos

  @bullet-38
  Scenario: A comment over the limit is cut
    Given the test gate is red after each of four builds
    And the test command writes the letter "a" 300000 times to its standard output
    When a caller starts a feedback run
    # run: printf '262144\na\n200000\n[cut]'
    Then FeedbackRun posts one comment of 262144 bytes or fewer in bytes, and the comment holds the letter "a" 200000 times in sequence and "[cut]"

  @bullet-39
  Scenario: The container is removed after the last reply
    When a caller starts a feedback run
    Then FeedbackRun removes the container after the last reply

  @bullet-40
  Scenario: A GraphQL error on a reply throws and removes the container
    Given GitHub answers the reply to the thread "PRRT_kwDOUpm1Qc6ofWu5" with the status code 200 and a list errors
    When a caller starts a feedback run
    Then FeedbackRun throws GitHubGraphQLException
    And FeedbackRun removes the container
