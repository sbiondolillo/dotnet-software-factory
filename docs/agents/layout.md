# Layout

Where things are in this repo, for an agent about to read or write code here.

## Directories

`docs/agents/setup-sheet.toml` names the spec directory and the binding directory. It is the authority for those two. This table says what each directory holds.

| path | holds | rule |
|---|---|---|
| `specs/features/` | the pinned `.feature` files | a build reads them and never edits them. `tests/Specs/Specs.csproj` links every one of them in |
| `tests/Specs/` | the build suite, hermetic: Reqnroll step classes under `Steps/`, and xUnit unit tests under `Unit/` | a unit test class name ends `Tests`. A class name that ends `Feature` belongs to Reqnroll's generated code |
| `tests/Integration/` | the integration suite: xUnit tests against the real GitHub, `docker` and Claude Code | `IsTestProject` is false, so `dotnet test` never collects it. `dotnet run --project tests/Integration` runs it. A test fails when its service is unreachable |
| `src/Core/` | the library, where the behaviour lives | the test project references it |
| `src/App/` | the console program, a thin shell over the library | |

## How a scenario reaches the code

Reqnroll compiles each linked feature into a test class under `obj/`, named after the feature title with `Feature` added, and one test method per scenario. A step class marked `[Binding]` answers the steps. It calls the library in process.

## The rules that build and style enforce

`Directory.Build.props` turns on nullable reference types, the recommended analyzers and `EnforceCodeStyleInBuild`, and makes every warning an error. `.editorconfig` holds the style. `dotnet format` fixes what it can.

The house style as prose, for the rules no analyzer expresses: a class that stores its constructor arguments uses a primary constructor, and a comparison is a pattern: `value is null`, `value is not null`, `result.ExitCode is not 0`, `value is string text`. `is not 0` over `!= 0` is the one rule no analyzer flags; the build will not catch it, so write it by hand.
