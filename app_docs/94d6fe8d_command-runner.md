# Document — CommandRunner (run 94d6fe8d)

## What changed and why it matters

The factory starts other programs (`git`, `docker`, …) and reads what they print, but the
library had no place that starts a program. This run adds `CommandRunner` in `src/Core`,
the one place in the library that does that. Its `RunAsync` takes the program, its
arguments, a working directory, extra environment variables and a timeout, and returns a
`CommandResult` holding the exit code, the standard output and the standard error.

The design points that matter for callers:

- **No shell.** Arguments go through `ProcessStartInfo.ArgumentList`, never a shell, so an
  argument with a space, a double quote or a `$` reaches the program character for
  character, and no shell is ever spawned even for a program that is not on the PATH.
- **Streams are read concurrently** (two `ReadToEndAsync` under `Task.WhenAll`). A program
  that fills both 64 KB pipe buffers would deadlock on sequential reads; with 1 MB on each
  stream, `RunAsync` returns with both outputs whole.
- **Timeouts stop the program.** If the timeout passes with the program still running, the
  whole process tree is killed and `RunAsync` throws `System.TimeoutException` whose message
  names the program and the timeout (whole-second timeouts read as `N seconds`). A program
  that cannot start throws `InvalidOperationException` naming the program, with the start
  exception as the inner exception.
- **Environment handling.** Extra variables are set on `ProcessStartInfo.Environment`, which
  replaces an inherited variable of the same name while leaving the inherited variables it
  does not mention visible.
- **Mockable.** `CommandRunner` is a concrete, non-sealed class and `RunAsync` is `virtual`,
  so caller code is tested by mocking `RunAsync` with Moq, exactly as the acceptance text
  requires.

## The files that carry it

| file | what it is |
|---|---|
| `src/Core/CommandResult.cs` | `public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)`. |
| `src/Core/CommandRunner.cs` | The runner: start, concurrent stream reads, working directory, extra environment variables, timeout with process-tree kill, start-failure handling. |
| `specs/features/command-runner.feature` | The pinned feature: 11 scenarios covering exit code, both streams, 1 MB output, character-for-character arguments, working directory, environment override and inheritance, timeout, and a missing program. |
| `specs/94d6fe8d_acceptance_raw.md` | The acceptance text the feature was cut from. |
| `plans/94d6fe8d_implement-command-runner.md` | The plan, including a Soundness section that ran every scenario's command in the sandbox before binding. |
| `tests/Specs/Steps/CommandRunnerSteps.cs` | The `[Binding]` step class for the feature; it drives the real `CommandRunner` in process. It parses the quoted argument spans from the step text, sets inherited variables with `Environment.SetEnvironmentVariable`, and checks "the program is no longer running" by snapshotting live `sleep` PIDs from `/proc` before the run and asserting none are new after it. |
| `tests/Specs/Unit/CommandRunnerTests.cs` | Five xUnit facts: the timeout stops the program and names it, a missing program throws naming it, the two streams never mix, 1 MB on each stream arrives whole, and a Moq `Mock<CommandRunner>` hands a canned `CommandResult` to the caller. |
| `tests/Specs/XunitAssemblyInfo.cs` | `[assembly: CollectionBehavior(DisableTestParallelization = true)]` — xUnit v3 runs test classes in parallel by default, and a concurrently running unit-test `sleep` would pollute the feature's PID-snapshot check. |

No project file or package changed: `System.Diagnostics.Process` is in the shared framework,
and `Specs.csproj` already references `Core.csproj` and Moq. `tests/Integration/` is
untouched because the scenarios use local binaries, no canned answers.

## How to use it

```csharp
var result = await new CommandRunner().RunAsync(
    "git", new[] { "status", "--porcelain" },
    workingDirectory: "/path/to/repo",
    extraEnvironmentVariables: new Dictionary<string, string> { ["GIT_PAGER"] = "cat" },
    timeout: TimeSpan.FromSeconds(30));
// result.ExitCode, result.StandardOutput, result.StandardError
```

Catch `TimeoutException` when the program must not outlive its budget; catch
`InvalidOperationException` (and inspect its `InnerException`) when the program cannot be
started at all.

## How to verify it

From the repo root:

```
dotnet build --no-restore
dotnet test --no-restore
```

This was run in the sandbox: all 17 tests pass — the 11 command-runner scenarios, the
existing greeting scenario, and the 5 new unit tests. `dotnet format --verify-no-changes
--no-restore` is the style gate; warnings are errors.

## What the diff left unexplained

Nothing material. The only choices worth flagging for a future reader:

- The timeout message formats whole seconds as `N seconds` (not `2.0000000 seconds`)
  because the pinned scenario asserts the literal `2 seconds`.
- `Start()` is wrapped so that any start failure becomes `InvalidOperationException`
  naming the program; the feature only pins the message content, so that wrapper is the
  implementation's choice.
- The PID-snapshot check reads `/proc/*/comm` directly, which is Linux-only — consistent
  with the acceptance text's "the factory runs on Linux" and with the tests running where
  `sh` is on the PATH.
