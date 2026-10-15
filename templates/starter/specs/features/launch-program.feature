Feature: Launching the program

  Scenario: The launched program greets by name
    When I launch the built program with the argument "Ada"
    Then the program exits with the code 0
    And the program writes "Hello, Ada!" to standard output
