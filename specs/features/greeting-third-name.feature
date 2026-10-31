Feature: Greeting a third name

  Scenario: The greeter greets a third name
    Given the name "Linus"
    When the greeter greets
    Then the greeting is "Hello, Linus!"
