Feature: Greeting a second name

  Scenario: The greeter greets a second name
    Given the name "Grace"
    When the greeter greets
    Then the greeting is "Hi, Grace!"
