As a person who runs the console program, I want to pass `--shout` so that the greeting prints in upper case.

**Today.** The console program prints `Hello, <name>!` and has no option that changes its case. `--shout` is an unknown option, and System.CommandLine answers it with an error about an unrecognized argument.

**What changes.** The command tree of `src/Core/GreetCommand.cs` gains the option `--shout`. The console program of `src/App/` runs with it and stays as it is. `specs/features/greeting.feature` gains three scenarios, and `tests/Specs/Steps/GreetingSteps.cs` binds the new steps. Every other existing test passes unchanged.

## Scenarios

```gherkin
# specs/features/greeting.feature
  Scenario: The console program shouts the greeting of a name
    When the console program runs with the arguments "Ada --shout"
    Then the console program prints "HELLO, ADA!"

  Scenario: The console program shouts the greeting of the world
    When the console program runs with the arguments "--shout"
    Then the console program prints "HELLO, WORLD!"

  Scenario: The help lists the option shout
    When the console program runs with the arguments "--help"
    Then the console program prints a line that contains "--shout"
```

