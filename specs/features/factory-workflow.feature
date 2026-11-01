Feature: The factory workflow reads the builder's ids from the lockfile

  Scenario: The lockfile step of the factory workflow writes the builder's ids to the job environment
    When the step "Read the builder's ids from the lockfile" of ".github/workflows/factory.yml" runs on the lockfile ".github/agents/claude-lock.json" of this repo
    Then the step exits with the code 0
    And the job environment file holds the line "FACTORY_AGENT_ID=agent_011WNxCWuyav7pkMT38nR5Sx"
    And the job environment file holds the line "FACTORY_ENVIRONMENT_ID=env_01HfX9b6TUTq88cnHfFjU4Bc"

  Scenario: The lockfile step of the factory workflow fails when the lockfile has no environment id
    When the step "Read the builder's ids from the lockfile" of ".github/workflows/factory.yml" runs on a lockfile that holds only the agent "./builder.md" with the id "agent_x"
    Then the step exits with the code 5
    And the error output of the step holds "claude-lock.json has no id for builder.environment.yaml"
