# CommandRunner with a timeout

## What changed and why it matters

The factory starts other programs (`git`, `docker`, ...) and reads what they print. This change adds that one place — `CommandRunner` in `src/Core` — plus its result type, a pinned BDD feature for it, a Reqnroll step class, and unit tests for the sad paths.

`CommandRunner.RunAsync(program, arguments, workingDirectory, environment, timeout)` starts a program and returns a `CommandResult` holding the exit code, standard output and standard error. Key behaviours:

- **No shell quoting games.** Arguments go into `ProcessStartInfo.ArgumentList` one by one, so an argument containing a space, a double quote or a `$` reaches the child character for character (`src/Core/CommandRunner.cs:26-29`).
- **Streams are read concurrently** before waiting, so a child that writes more than the pipe buffer (the 1 MB scenario) cannot deadlock the run (`src/Core/CommandRunner.cs:60-62`).
- **Timeout.** A `CancellationTokenSource` is cancelled after the timeout; on `OperationCanceledException` the whole process tree is killed, reaped, and a `TimeoutException` is thrown naming the program and the timeout (whole seconds as "1 second"/"N seconds", sub-second as "N milliseconds") (`src/Core/CommandRunner.cs:50-55, 63-72, 80-90`).
- **Missing program.** A `Win32Exception` at start is translated to `InvalidOperationException` whose message names the program (`src/Core/CommandRunner.cs:57-62`).
- **Environment.** Extra variables are set on the start info, overriding inherited ones of the same name and leaving the rest in place (`src/Core/CommandRunner.cs:38-44`).
- A non-zero exit is *data, not a fault*: `RunAsync` returns a `CommandResult` with the exit code rather than throwing.

`CommandRunner` is a concrete class and `RunAsync` is `virtual`, so callers are testable by mocking `RunAsync` with Moq.

## Files that carry it

| file | role |
|---|---|
| `src/Core/CommandRunner.cs` | the runner: `virtual RunAsync` as above |
| `src/Core/CommandResult.cs` | `sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)` |
| `specs/features/command-runner.feature` | the pinned feature, 11 scenarios covering exit code, stdout, stderr, 1 MB both streams, verbatim arguments, working directory, extra/inherited environment, timeout + tree kill, missing program |
| `tests/Specs/Steps/CommandRunnerSteps.cs` | `[Binding]` step class answering all 11 scenarios against real `sh`/`python3` |
| `tests/Specs/Unit/CommandRunnerTests.cs` | xUnit tests for the three sad paths: non-zero exit returns data, timeout throws `TimeoutException` (and the sleeper's pid is dead afterwards), missing program throws naming it |
| `specs/28da2742_acceptance_raw.md` | the raw acceptance criteria this run built against |
| `plans/28da2742_command-runner-timeout.md` | the verified build plan, including the exact per-scenario programs and the CA1822/CA1805/CA1305 gotchas that fired in the trial build |

## How to use or verify it

- `dotnet build --no-restore` — every warning is an error; the code was written clean of the CA1822/CA1805/CA1305 rules that fire in this repo.
- `dotnet test --no-restore` — expect the 11 `command-runner.feature` scenarios (plus the greeting scenario) and the 3 unit tests, all green. The steps and unit tests launch real `sh` (and `python3` for the 1 MB scenario), both on PATH in the sandbox image.
- `dotnet format --verify-no-changes --no-restore`.

Note: the timeout step and its unit test write a Guid-named pidfile to the temp directory to verify the killed sleeper is actually dead; nothing else is left on disk.
