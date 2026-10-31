Feature: Greeting

  Scenario: The greeter greets by name
    Given the name "Ada"
    When the greeter greets
    Then the greeting is "Hello, Ada!"
