Feature: ClaudeCodeAgent runs one prompt in the sandbox and reads a JSON file the agent wrote

  Scenario: The docker exec arguments carry claude, the prompt, the model and the turn limit
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And docker exec exits with the code 0 and writes to standard output '{"is_error":false,"num_turns":1,"total_cost_usd":0.0098972,"result":"ok"}'
    When a caller runs the prompt "Reply with the single word ok and nothing else." with the model "claude-haiku-4-5-20251001" and the turn limit 4
    Then the docker exec arguments hold "claude" right after the container id, then "-p" and then "Reply with the single word ok and nothing else.", "--model" and then "claude-haiku-4-5-20251001", and "--max-turns" and then "4"

  Scenario: A successful answer gives back its four values
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And docker exec exits with the code 0 and writes to standard output '{"type":"result","subtype":"success","is_error":false,"num_turns":1,"total_cost_usd":0.0098972,"result":"ok"}'
    When a caller runs the prompt "Reply with the single word ok and nothing else." with the model "claude-haiku-4-5-20251001" and the turn limit 1
    Then the result error flag is false
    And the result has the turn count 1, the cost 0.0098972 and the result text "ok"

  Scenario: An error answer with no result member gives back an empty result text
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And docker exec exits with the code 1 and writes to standard output '{"type":"result","subtype":"error_max_turns","is_error":true,"num_turns":2,"total_cost_usd":0.0044689,"errors":[]}'
    When a caller runs the prompt "Write a file." with the model "claude-haiku-4-5-20251001" and the turn limit 1
    Then the result error flag is true
    And the result has the turn count 2, the cost 0.0044689 and an empty result text

  Scenario: An error answer with a sentence gives back that sentence
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And docker exec exits with the code 1 and writes to standard output '{"type":"result","is_error":true,"num_turns":1,"total_cost_usd":0,"result":"Invalid API key"}'
    When a caller runs the prompt "Say hi." with the model "claude-haiku-4-5-20251001" and the turn limit 1
    Then the result error flag is true
    And the result has the turn count 1 and the cost 0
    And the result text is the sentence the JSON object holds

  Scenario: A failed start with no JSON object throws ClaudeCodeException with the exit code and the reason
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And docker exec exits with the code 1, writes nothing to standard output and writes a reason to standard error
    When a caller runs the prompt "Say hi." with the model "claude-haiku-4-5-20251001" and the turn limit 1
    Then the run throws ClaudeCodeException
    And the exception message holds "1" and the reason

  Scenario: A prompt of 131071 letters reaches docker exec whole
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And docker exec exits with the code 0 and writes to standard output '{"is_error":false,"num_turns":1,"total_cost_usd":0.001,"result":"ok"}'
    When a caller runs a prompt of 131071 characters, each one the letter "a", with the model "claude-haiku-4-5-20251001" and the turn limit 1
    Then the docker exec arguments hold "-p" and then that prompt

  Scenario: A prompt of 131072 letters throws ClaudeCodeException and runs no docker exec
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    When a caller runs a prompt of 131072 characters, each one the letter "a", with the model "claude-haiku-4-5-20251001" and the turn limit 1
    Then the run throws ClaudeCodeException
    And the exception message holds "131072"
    And the runner runs no docker exec

  Scenario: A file holding one JSON object is read as a JSON document
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And a file at a path holds '{"verdict":"approved","items":[]}'
    When the caller reads the JSON file at the path
    Then the document root has the property "verdict" with the value "approved" and the property "items" with 0 elements

  Scenario: A missing file throws ClaudeCodeOutputException naming the path
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And no file is at a path
    When the caller reads the JSON file at the path
    Then the read throws ClaudeCodeOutputException
    And the exception message holds the path

  Scenario: A file holding not json throws ClaudeCodeOutputException naming the path
    Given a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner
    And a file at a path holds 'not json'
    When the caller reads the JSON file at the path
    Then the read throws ClaudeCodeOutputException
    And the exception message holds the path
