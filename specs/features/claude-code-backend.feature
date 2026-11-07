Feature: ClaudeCodeBackend runs the builder and the reviewer as one agent in one sandbox, which also runs the gates

  Background:
    Given a Claude Code backend over a clone

  Scenario: The start writes the line of the sandbox before it inspects the image
    When the Claude Code backend starts
    Then the progress of the Claude Code backend held the line "sandbox: start" when docker got the command "image inspect"

  Scenario: The container holds the two Anthropic variables by name
    When the Claude Code backend starts
    Then the docker run of the Claude Code backend holds "--env" and then "ANTHROPIC_API_KEY", and "--env" and then "ANTHROPIC_BASE_URL"

  Scenario: The builder and the reviewer run claude in the container of the gates and report each answer to the run
    Given claude answers each prompt of the Claude Code backend with 7 turns and the cost 0.4213
    When the Claude Code backend starts
    And its builder answers a plan prompt
    And its reviewer answers a review prompt
    Then each prompt ran claude through docker exec in the container of the gates
    And the Claude Code backend reported 2 answers, each of 7 turns and the cost 0.4213 dollars
    And the log of the Claude Code backend holds the line "agent: plan start"

  Scenario: The collect step runs no command
    When the Claude Code backend starts
    And the Claude Code backend collects onto the branch "factory/a1b2c3d4"
    Then the Claude Code backend ran no command after its start

  Scenario: The end removes the container
    When the Claude Code backend starts
    And the Claude Code backend ends
    Then the last command of the Claude Code backend is "docker rm --force" of its container
