# AGENTS.md

The rules for an agent that reads, writes or reviews code in this repo.

## Layout

| path | holds | rule |
|---|---|---|
| `specs/features/` | the `.feature` files | `tests/Specs/Specs.csproj` links every one of them in |
| `tests/Specs/` | the build suite: Reqnroll step classes under `Steps/`, and xUnit unit tests under `Unit/` | each test answers every call to a service from a stub. A unit test class name ends `Tests` |
| `tests/Integration/` | the integration suite: xUnit tests against the real services that the program calls | `dotnet run --no-restore --project tests/Integration` runs it, and `dotnet test` leaves it out |
| `src/Core/` | the library, where the behaviour lives, and the `System.CommandLine` command tree | the tree holds every command, option, argument and action. A new option or argument is a symbol in the tree, and `--help` lists it |
| `src/App/` | the console program, a thin shell over the library | `Program.cs` builds the Generic Host, `Microsoft.Extensions.Hosting`, and parses its arguments with the command tree from `src/Core` |

## How a scenario reaches the code

Reqnroll compiles each linked feature into a test class under `obj/`, named after the feature title with `Feature` added, and one test method per scenario. A class name that ends `Feature` belongs to that generated code. A step class marked `[Binding]` answers the steps, and it calls the library in process.

## The integration suite

The build suite answers each call to a service with a canned answer from a stub `HttpMessageHandler`. For each canned answer that a scenario depends on, write a test under `tests/Integration/` that gets the same answer from the real service. The test fails when the service is unreachable. A test reads each credential from the environment, by the name that the `integration` job of `.github/workflows/ci.yml` passes.

## Style

`Directory.Build.props` makes every warning an error and enforces the style of `.editorconfig` in the build. `dotnet format` fixes what it can.

- A class that stores its constructor arguments uses a primary constructor. IDE0290.
- A comparison with a constant is a pattern: `value is null`, `value is not null`, `exitCode is not 0`, `value is string text`. IDE0041, IDE0083, IDE0078, IDE0020 and IDE0019 flag most cases. Write `is not 0` in place of `!= 0` by hand, because no analyzer flags it.
