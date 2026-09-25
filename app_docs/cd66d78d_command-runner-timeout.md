# CommandRunner: run a program with a timeout, return its exit code and output

## What changed and why it matters

The factory starts other programs (`git`, `docker`) and reads what they print, but the library had no place that starts a process. This change adds that one place: `Core.CommandRunner.RunAsync`, which starts a program with its arguments, an optional working directory, extra environment variables and an optional timeout, and returns a `CommandResult` holding the exit code, standard output and standard error.

Behaviour worth knowing about:

- **Arguments are passed as a list, never a shell string.** The process is started directly (`ArgumentList`, no `sh -c` wrapper), so an argument containing spaces, `"` or `$` arrives character for character.
- **Extra environment variables layer over the inherited environment.** An extra name replaces an inherited variable of the same name; an inherited name the extras leave out stays visible with its inherited value.
- **Both streams are read concurrently**, so a 1 MB write to either stream cannot fill the pipe buffer and deadlock the program. Captured streams have trailing line endings trimmed: `hello world\n` yields `hello world`, and a program that writes exactly N characters with no trailing newline still yields N characters.
- **Timeout.** When the timeout elapses first, the whole process tree is killed and `RunAsync` throws `TimeoutException` with a message of the form `Program 'sh' timed out after 1000 milliseconds` (names the program and the timeout).
- **Start failure.** A program on no PATH makes `RunAsync` throw `InvalidOperationException` whose message names the program, with the original exception attached.
- `CommandRunner` is a concrete class and `RunAsync` is `virtual`, so calling code can mock it with Moq.

## Files that carry it

- `src/Core/CommandRunner.cs` — `RunAsync` and all the behaviour above (the ~166-line core of the change).
- `src/Core/CommandResult.cs` — the result type: `ExitCode`, `StandardOutput`, `StandardError` as init properties.
- `specs/features/command_runner.feature` — the pinned spec: 11 scenarios, each with a `#` comment recording the shell command and its expected output that was computed to verify the scenario.
- `specs/cd66d78d_acceptance_raw.md` — the raw acceptance text the feature was written from.
- `plans/cd66d78d_command-runner-timeout.md` — the build plan, including the per-scenario soundness check.
- `tests/Specs/Steps/CommandRunnerSteps.cs` — the Reqnroll `[Binding]` class that binds all 11 scenarios; every `When` calls the real `CommandRunner` in process.
- `tests/Specs/Unit/CommandRunnerTests.cs` — four xUnit facts driving the runner directly: exit code 3, stderr isolation, timeout (200 ms, to keep the suite fast), and a no-PATH program.

No canned answers anywhere: every scenario and fact runs real `sh`, `pwd` and `python3` in process, so nothing was added to `tests/Integration/`.

## How to use it

```csharp
var result = await runner.RunAsync("git", ["status"], timeout: TimeSpan.FromSeconds(30));
var code = result.ExitCode;
var outText = result.StandardOutput;
var errText = result.StandardError;
```

## How to verify

From the repo root (packages already restored):

- `dotnet test --no-restore` — all 11 `CommandRunnerFeature` scenarios pass, plus the four new unit tests and the existing `GreetingFeature`.
- `dotnet build --no-restore` — 0 warnings (warnings are errors); `dotnet format --verify-no-changes --no-restore` passes.
