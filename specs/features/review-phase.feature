Feature: The review phase approves or rejects the build with blocking items

  Background:
    Given the issue has the title "Add a checkout total" and the body "The checkout shows the total of the cart."
    And the review model is "claude-haiku-4-5-20251001" and the turn limit is 30
    And the base commit is "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And claude answers with the error flag clear

  @bullet-1
  Scenario: Git gives back the diff text that git answers
    Given git answers the diff with the text "diff --git a/src/Core/Checkout.cs b/src/Core/Checkout.cs"
    When I ask Git for the diff of the work tree against the base commit
    # run: printf '%s' 'diff --git a/src/Core/Checkout.cs b/src/Core/Checkout.cs'
    Then Git gives back the text "diff --git a/src/Core/Checkout.cs b/src/Core/Checkout.cs"

  @bullet-1
  Scenario: Git fails when the base names no commit
    Given git exits with the code 128 for the diff
    When I ask Git for the diff of the work tree against the base commit
    Then Git throws a GitCommandException

  @bullet-2
  Scenario: The review prompt carries the issue, the model and the turn limit
    Given the agent writes '{"approved": true}' as the verdict
    When I run the review phase
    Then the review phase ran one prompt
    # run: printf '%s\n' 'claude-haiku-4-5-20251001' 30
    And the review prompt ran with the model "claude-haiku-4-5-20251001" and the turn limit 30
    # run: printf '%s\n' 'Add a checkout total' 'The checkout shows the total of the cart.'
    And the review prompt holds "Add a checkout total" and "The checkout shows the total of the cart."

  @bullet-3
  Scenario: The review prompt names the features, the plan and the verdict file
    When I run the review phase
    # run: printf '%s\n' 'specs/features/' '.factory/plan.md' '.factory/review.json'
    Then the review prompt holds "specs/features/", ".factory/plan.md" and ".factory/review.json"

  @bullet-4
  Scenario: The review prompt names the verdict fields
    When I run the review phase
    # run: printf '%s\n' 'approved' 'blocking'
    Then the review prompt holds "approved" and "blocking"

  @bullet-5
  Scenario: The review prompt holds the base commit and the diff
    Given git answers the diff with the text "diff --git a/src/Core/Checkout.cs b/src/Core/Checkout.cs"
    When I run the review phase
    # run: printf '%s\n' '4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c' 'diff --git a/src/Core/Checkout.cs b/src/Core/Checkout.cs'
    Then the review prompt holds "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c" and "diff --git a/src/Core/Checkout.cs b/src/Core/Checkout.cs"

  @bullet-6
  Scenario: A long diff is cut so the prompt fits one argument
    Given git answers the diff with the letter "a" repeated 200000 times
    When I run the review phase
    Then the review phase ran one prompt
    # run: printf '%s\n' 131071
    And the review prompt is 131071 bytes or fewer when encoded
    # run: printf '%s\n' 'Add a checkout total' 'The checkout shows the total of the cart.' '4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c'
    And the review prompt holds "Add a checkout total", "The checkout shows the total of the cart." and "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    # run: printf '%s\n' a 100000
    And the review prompt holds the letter "a" 100000 times in sequence
    # run: printf '%s\n' '[cut]'
    And the review prompt holds "[cut]"

  @bullet-7
  Scenario: A long diff of two-byte letters is cut on a whole letter
    Given git answers the diff with the letter "é" repeated 100000 times
    When I run the review phase
    Then the review phase ran one prompt
    # run: printf '%s\n' 131071
    And the review prompt is 131071 bytes or fewer when encoded
    # run: printf '%s\n' é 50000
    And the review prompt holds the letter "é" 50000 times in sequence
    # run: printf '%s\n' '[cut]'
    And the review prompt holds "[cut]"

  @bullet-8
  Scenario: An approval with an empty blocking list
    Given the agent writes '{"approved": true, "blocking": []}' as the verdict
    When I run the review phase
    Then the review gives back an approval

  @bullet-9
  Scenario: An approval with no blocking field
    Given the agent writes '{"approved": true}' as the verdict
    When I run the review phase
    Then the review gives back an approval

  @bullet-10
  Scenario: A rejection with one blocking item
    Given the agent writes '{"approved": false, "blocking": ["The total ignores the second item of the cart."]}' as the verdict
    When I run the review phase
    # run: printf '%s' 'The total ignores the second item of the cart.'
    Then the review gives back a rejection whose one blocking item is "The total ignores the second item of the cart."

  @bullet-11
  Scenario: Approved with a blocking item is a rejection
    Given the agent writes '{"approved": true, "blocking": ["The total ignores the second item of the cart."]}' as the verdict
    When I run the review phase
    # run: printf '%s' 'The total ignores the second item of the cart.'
    Then the review gives back a rejection whose one blocking item is "The total ignores the second item of the cart."

  @bullet-12
  Scenario: Not approved with no blocking item is a rejection naming the file
    Given the agent writes '{"approved": false, "blocking": []}' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-13
  Scenario: No verdict file is a rejection naming the file
    Given the agent writes no verdict file
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-14
  Scenario: A verdict file of an earlier review does not give the verdict
    Given the work tree holds '{"approved": true, "blocking": []}' in the verdict file before the review
    And the agent writes no verdict file
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-15
  Scenario: A verdict file that is not JSON is a rejection
    Given the agent writes 'The build meets the issue.' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-16
  Scenario: A verdict file with no approved field is a rejection
    Given the agent writes '{"blocking": []}' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-17
  Scenario: A verdict file with a text approved is a rejection
    Given the agent writes '{"approved": "yes", "blocking": []}' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-18
  Scenario: A verdict file that is a list is a rejection
    Given the agent writes '[true]' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-19
  Scenario: A blocking field that is a text is a rejection
    Given the agent writes '{"approved": false, "blocking": "The total ignores the second item of the cart."}' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-20
  Scenario: A blocking list holding a number is a rejection
    Given the agent writes '{"approved": false, "blocking": [3]}' as the verdict
    When I run the review phase
    # run: printf '%s' '.factory/review.json'
    Then the review gives back a rejection with one blocking item that holds ".factory/review.json"

  @bullet-21
  Scenario: An error answer from claude is a rejection with its result text
    Given the agent writes '{"approved": true, "blocking": []}' as the verdict
    And claude answers with the error flag set and the result text "Invalid API key"
    When I run the review phase
    # run: printf '%s' 'Invalid API key'
    Then the review gives back a rejection with one blocking item that holds "Invalid API key"

  @bullet-22
  Scenario: An error answer from claude with no result text is a rejection
    Given claude answers with the error flag set and no result text
    When I run the review phase
    Then the review gives back a rejection with one blocking item
