Feature: ManagedAgentsAgent runs each prompt as one message of a Managed Agents session

  Background:
    Given a stub Managed Agents API whose session id is "sesn_1" and whose repository resource id is "sesrsc_1"
    And the repo is "sbiondolillo/factory-scratch", the session branch is "factory/7-session" and the base commit is "abc1234"

  Scenario: The first prompt creates a session with the model, the repository, the token, the budget and the prompt
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the API got one POST to "/v1/sessions"
    And the session body names the agent "agent_test" with the model "claude-sonnet-5-5" and the environment "env_test"
    And the session body has one github_repository resource with the URL "https://github.com/sbiondolillo/factory-scratch", the mount path "/workspace/factory-scratch" and the first minted token
    And the session body has the budget "1500" in "USD"
    And the session body has one initial user.message whose text holds "Build the change."
    And the first token was minted with write access

  Scenario: Every request carries the API key, the version and the beta header
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then every request to the API carries the header "x-api-key" with "sk-test", "anthropic-version" with "2023-06-01" and an "anthropic-beta" that holds "managed-agents-2026-04-01"

  Scenario: A second prompt is one user.message to the same session
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the API got one POST to "/v1/sessions"
    And the API got one POST to "/v1/sessions/sesn_1/events" with one user.message whose text holds "Fix the build."

  Scenario: A second prompt with another model throws ClaudeCodeException and posts nothing
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the caller runs the prompt "Fix the build." with the model "claude-opus-5-5"
    Then the run throws ClaudeCodeException
    And the exception message holds "claude-opus-5-5"
    And the API got no POST to "/v1/sessions/sesn_1/events"

  Scenario: The preamble checks out and pulls the session branch as the bot, with .factory excluded, before the prompt
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the first message holds these texts in this order:
      | text |
      | cd /workspace/factory-scratch |
      | git config user.name "dsf-repair[bot]" |
      | git config user.email "350419598+dsf-repair[bot]@users.noreply.github.com" |
      | echo '.factory/' >> .git/info/exclude |
      | git fetch --depth=400 origin +refs/heads/factory/7-session:refs/remotes/origin/factory/7-session abc1234 |
      | git checkout factory/7-session |
      | git pull --ff-only origin factory/7-session |
      | rm -f .factory/review.json .factory/threads.json |
      | git status --porcelain |
      | Build the change. |
    And the first message holds "bash /tmp/dotnet-install.sh --jsonfile global.json --install-dir /root/.dotnet"
    And the first message holds "/mnt/session/outputs/"

  Scenario: The builder's tail commits and pushes to the session branch after the prompt
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the first message holds these texts in this order:
      | text |
      | Build the change. |
      | git status --porcelain |
      | git push origin HEAD:factory/7-session |
      | outputs: |

  Scenario: The reviewer sends no tail, and its token is read-only
    Given a reviewer agent with the budget 300 cents
    When the caller runs the prompt "Review the change." with the model "claude-sonnet-5-5"
    Then the first message does not hold "git push"
    And the first message does not hold "git commit"
    And the first message holds these texts in this order:
      | text |
      | Review the change. |
      | outputs: |
    And the first token was minted with read access

  Scenario: The answer text is the last agent.message of the turn
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "Working." and "Done, the build passes."
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the answer text is "Done, the build passes."
    And the answer error flag is false

  Scenario: The line outputs: is not part of the answer text
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "Done.\noutputs:"
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the answer text is "Done."

  Scenario: The turn count is the count of span.model_request_end events of the turn, over pages of the event list
    Given a builder agent with the budget 1500 cents
    And the event list answers 2 events a page
    And the next turn holds 5 model requests
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the next turn holds 3 model requests
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the answer has the turn count 3

  Scenario: The cost of each answer is the delta of list_cost in dollars, and Sessions holds the final cents
    Given a builder agent with the budget 1500 cents
    And the next turn ends with the session cost at "123" cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the next turn ends with the session cost at "180" cents
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the answer has the cost 0.57
    And Sessions holds one session "sesn_1" with the role "builder" and 180 cents

  Scenario: A turn that reaches the budget is an error answer that names the budget
    Given a builder agent with the budget 1500 cents
    And the next turn ends with the stop reason "budget_reached"
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the answer error flag is true
    And the answer text holds "budget"
    And the answer text holds "1500"

  Scenario: A session.status_idle before the posted message does not end the turn
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the next turn holds the agent messages "Fixed."
    And the next message shows in the event list only after 2 reads, and its idle after 2 more
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the answer text is "Fixed."
    And the agent waited 5 seconds between reads of the event list

  Scenario: Files created after the post land under .factory by base name, the newest of each name
    Given a builder agent with the budget 1500 cents
    And the session lists the file "plan.md" holding "old plan" created before the next message
    And the session lists the file "threads.json" holding "[]" created before the next message
    And the next turn creates the file "plan.md" holding "first plan" and then "plan.md" holding "new plan"
    And the next turn creates the file "review.json" holding "{}"
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the work tree file ".factory/plan.md" holds "new plan"
    And the work tree file ".factory/review.json" holds "{}"
    And the work tree has no file ".factory/threads.json"

  Scenario: A file the outputs line names that is late in the list is waited for
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "Done.\noutputs: plan.md"
    And the next turn creates the file "plan.md" holding "the plan", listed only after 3 reads of the file list
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the answer error flag is false
    And the work tree file ".factory/plan.md" holds "the plan"

  Scenario: A file the outputs line names that never lists is an error answer that names it
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "Done.\noutputs: plan.md"
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the answer error flag is true
    And the answer text holds "plan.md"
    And the agent waited no more than 60 seconds for the file list

  Scenario: The top-level files of .factory in the work tree are uploaded and mounted at the session start
    Given the work tree file ".factory/issue.md" holds "the issue"
    And the work tree file ".factory/sandbox-home/1/x.json" holds "{}"
    And a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the API got one POST to "/v1/files" whose body holds "the issue"
    And the session body has one file resource with the uploaded file id and the mount path "/issue.md"
    And the first message holds "cp /mnt/session/uploads/issue.md .factory/issue.md"

  Scenario: The events of each turn go to events.jsonl under the root directory, one JSON object a line
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "One."
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the next turn holds the agent messages "Two."
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the root file ".factory/sandbox-home/sesn_1/events.jsonl" holds every event of the session once, in the order the API processed them

  Scenario: A token older than 50 minutes is rotated with one POST to the resource before the send
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And 51 minutes pass
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the API got one POST to "/v1/sessions/sesn_1/resources/sesrsc_1" with the second minted token, before the POST to "/v1/sessions/sesn_1/events"
    And the first minted token was revoked

  Scenario: A token that is exactly 50 minutes old is rotated before the send
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And 50 minutes pass
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the API got one POST to "/v1/sessions/sesn_1/resources/sesrsc_1" with the second minted token, before the POST to "/v1/sessions/sesn_1/events"
    And the first minted token was revoked

  Scenario: A push that fails with Authentication failed rotates the token and asks for the push again
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "The push failed: fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch/'\noutputs: plan.md"
    And the next turn holds 2 model requests
    And the next turn ends with the session cost at "40" cents
    And the next turn creates the file "plan.md" holding "the plan"
    And the turn after it holds the agent messages "Pushed.\noutputs: plan.md"
    And the turn after it holds 1 model requests
    And the turn after it ends with the session cost at "55" cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the API got one POST to "/v1/sessions/sesn_1/resources/sesrsc_1" with the second minted token, before the POST to "/v1/sessions/sesn_1/events"
    And the first minted token was revoked
    And the API got one POST to "/v1/sessions/sesn_1/events" with one user.message whose text holds "git push origin HEAD:factory/7-session"
    And the answer text is "Pushed."
    And the answer error flag is false
    And the answer has the turn count 3
    And the answer has the cost 0.55
    And the work tree file ".factory/plan.md" holds "the plan"

  Scenario: A second Authentication failed after the rotation is the answer with an error
    Given a builder agent with the budget 1500 cents
    And the next turn holds the agent messages "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch/'\noutputs:"
    And the turn after it holds the agent messages "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch/'\noutputs:"
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the answer error flag is true
    And the answer text holds "Authentication failed"
    And the API got one POST to "/v1/sessions/sesn_1/resources/sesrsc_1"
    And the API got one POST to "/v1/sessions/sesn_1/events" with one user.message whose text holds "git push origin HEAD:factory/7-session"

  Scenario: A token younger than 50 minutes is kept
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And 49 minutes pass
    And the caller runs the prompt "Fix the build." with the model "claude-sonnet-5-5"
    Then the API got no POST to "/v1/sessions/sesn_1/resources/sesrsc_1"

  Scenario: A cancel in the wait, then the disposal, sends user.interrupt and revokes the token
    Given a builder agent with the budget 1500 cents
    And the next turn never goes idle, and the caller cancels the token at the third read of the event list
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the caller disposes the agent
    Then the run throws OperationCanceledException
    And the API got one POST to "/v1/sessions/sesn_1/events" with one user.interrupt
    And the first minted token was revoked

  Scenario: A turn that does not go idle within 6 hours throws ClaudeCodeException that names the session
    Given a builder agent with the budget 1500 cents
    And the next turn never goes idle
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    Then the run throws ClaudeCodeException
    And the exception message holds "sesn_1"
    And the exception message holds "6 hours"

  Scenario: The disposal of an idle session sends no interrupt
    Given a builder agent with the budget 1500 cents
    When the caller runs the prompt "Build the change." with the model "claude-sonnet-5-5"
    And the caller disposes the agent
    Then the API got no POST to "/v1/sessions/sesn_1/events"
    And the first minted token was revoked

  Scenario: The log shows the start line, the session line and the end line
    Given a builder agent with the budget 1500 cents
    And the next turn holds 4 model requests
    And the next turn ends with the session cost at "250" cents
    When the caller runs the prompt "Build the change." with the phase "build" and the model "claude-sonnet-5-5"
    Then the log holds these lines in this order:
      | text |
      | agent: build start |
      | session: start sesn_1 |
      | agent: build end, 4 turns, $2.5, 0 s |

  Scenario: A prompt of 200000 letters reaches the session whole
    Given a builder agent with the budget 1500 cents
    When the caller runs a prompt of 200000 letters "a" with the model "claude-sonnet-5-5"
    Then the first message holds the whole prompt
