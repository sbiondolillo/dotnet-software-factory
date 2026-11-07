Feature: ManagedBackend runs the builder and the reviewer as two sessions over a session branch, and the gates in a container with no key

  Background:
    Given a managed backend over a clone, with the session branch "factory/a1b2c3d4-session"

  Scenario: The start writes the line of the sessions before it starts the container
    When the backend starts
    Then the progress of the backend held the line "sessions: start" when the docker run started

  Scenario: The container of the gates holds no Anthropic variable and needs a restore before the lint gate
    When the backend starts
    Then the docker run of the backend holds neither "ANTHROPIC_API_KEY" nor "ANTHROPIC_BASE_URL"
    And the docker run of the backend mounts the clone at /work and names the image "factory-sandbox:test"
    And the sandbox of the backend needs a restore

  Scenario: The backend pushes the session branch before each prompt, and before the first session starts
    When the backend starts
    And the builder answers a plan prompt
    And the builder answers a build prompt
    And the reviewer answers a review prompt
    Then git pushes the branch "factory/a1b2c3d4-session" before each of the 3 prompts
    And git pushes the branch "factory/a1b2c3d4-session" before the first session starts

  Scenario: The first push commits a change of the work tree with the title of the run
    Given git reports a change in the work tree of the backend
    When the backend starts
    And the builder answers a plan prompt
    Then git commits the work tree with the message "Add a checkout total: the scenarios of the issue" before the first push of the session branch

  Scenario: The first push of a work tree with no change commits nothing
    When the backend starts
    And the builder answers a plan prompt
    Then git runs no commit before the first push of the session branch

  Scenario: The session of the builder pulls the session branch after each prompt, and the session of the reviewer does not
    When the backend starts
    And the builder answers a plan prompt
    And the builder answers a build prompt
    And the reviewer answers a review prompt
    Then git pulls the session branch 2 times, after the plan prompt and after the build prompt

  Scenario: Only a build prompt reads the head of the session branch, before it and after it
    When the backend starts
    And the builder answers a plan prompt
    And the builder answers a build prompt
    And the reviewer answers a review prompt
    Then git reads the head of the session branch once before the build prompt and once after it, and at no other time

  Scenario: A build after which the head of the session branch did not move gets the fault of no commit
    Given the session pushes no commit on the build
    When the backend starts
    And the builder answers a build prompt
    Then the answer of the build holds the fault "The build pushed no commit to the session branch. Commit the work and push it to that branch."

  Scenario: A build that pushes a commit gets no fault
    When the backend starts
    And the builder answers a build prompt
    Then the answer of the build holds no fault

  Scenario: A file under .factory/ on the session branch stops the first prompt before any session starts
    Given the session branch tracks the file ".factory/leak.txt"
    When the backend starts
    And the builder answers a plan prompt
    Then the prompt throws SessionBranchException with the text "The session committed files under .factory/, which only the factory writes: .factory/leak.txt. The run stops."
    And no session starts

  Scenario: A pull that brings a file under .factory/ stops the build that pulled it
    Given the pull after the build brings a commit that tracks the file ".factory/x"
    When the backend starts
    And the builder answers a build prompt
    Then the prompt throws SessionBranchException with the text "The session committed files under .factory/, which only the factory writes: .factory/x. The run stops."

  Scenario: The session of the builder gets its budget and the session of the reviewer the budget of a review
    When the backend starts
    And the builder answers a plan prompt
    And the reviewer answers a review prompt
    Then the session of the builder starts with a budget of 1500 cents
    And the session of the reviewer starts with a budget of 300 cents

  Scenario: The collect step checks out the branch and pulls the session branch into it
    When the backend starts
    And the builder answers a build prompt
    And the backend collects onto the branch "factory/a1b2c3d4"
    Then the last two git commands check out "factory/a1b2c3d4" and pull the session branch into it

  Scenario: The end deletes the session branch on origin and removes the container
    When the backend starts
    And the builder answers a plan prompt
    And the backend ends
    Then git deletes the session branch on origin as its last git command
    And the container of the backend is removed

  Scenario: A backend that ran no prompt pushes no session branch and deletes none
    When the backend starts
    And the backend ends
    Then git pushes no session branch and deletes none

  Scenario: The sessions of the backend list the builder first, each with its cost
    Given the session answers cost 112 cents for the plan, 300 cents for a build and 37 cents for a review
    When the backend starts
    And the builder answers a plan prompt
    And the builder answers a build prompt
    And the reviewer answers a review prompt
    Then the sessions of the backend are "sesn_01" of the builder at 412 cents and "sesn_02" of the reviewer at 37 cents

  Scenario: The backend reports each answer to the run with its cost
    Given the session answers cost 112 cents for the plan, 300 cents for a build and 37 cents for a review
    When the backend starts
    And the builder answers a plan prompt
    And the reviewer answers a review prompt
    Then the backend reported 2 answers, of the costs 1.12 and 0.37 dollars
