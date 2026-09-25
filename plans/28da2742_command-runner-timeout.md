# Plan: CommandRunner with a timeout (command-runner.feature)

The pinned specs/features/command-runner.feature (11 scenarios) needs CommandRunner in src/Core: a concrete class
with a virtual RunAsync returning a CommandResult (exit code, standard output, standard error), plus a Reqnroll
step class and unit tests. Every design point below was built and run green (12/12) in a throwaway copy of this
repo, so the plan is verified, not proposed. No package or project-file changes are needed anywhere.

## Files to touch

- src/Core/CommandResult.cs (new): `public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);`
- src/Core/CommandRunner.cs (new): `public class CommandRunner` with `public virtual async Task<CommandResult> RunAsync(`
  `string program, IReadOnlyList<string>? arguments = null, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null)`:
  - ProcessStartInfo: FileName = program, UseShellExecute = false, RedirectStandardOutput = RedirectStandardError = true, each argument into ArgumentList (no shell: `a b "c" $d` must reach the child character for character), WorkingDirectory if given, one Environment entry per extra variable.
  - The extra dictionary overrides inherited variables and leaves the rest in place (verified), so S7-S9 need no other mechanism.
  - Start in try/catch: on Win32Exception (System.ComponentModel) throw `InvalidOperationException($"Program '{program}' was not found on PATH")` — the message must name the program.
  - Before waiting, kick off `Task<string> output = StandardOutput.ReadToEndAsync()` and `error = StandardError.ReadToEndAsync()`: the concurrent reads keep the 1MB scenario from deadlocking on the pipe buffer.
  - `await WaitForExitAsync(cts.Token)` with `cts.CancelAfter(timeout)`; on OperationCanceledException: `Kill(entireProcessTree: true)` (kills sh and its sleep child), reap with `await WaitForExitAsync()`, throw `TimeoutException($"Program '{program}' did not finish within {FormatTimeout(timeout)}")`.
  - FormatTimeout: whole seconds → "1 second" / "N seconds" (the S10 message must contain "1 second"), sub-second → "N milliseconds".
- tests/Specs/Steps/CommandRunnerSteps.cs (new): [Binding], namespace Specs.Steps, fixture fields, async When steps that catch into an exception field for S10/S11. Per-scenario programs (all verified):
  - S1 `sh -c "echo hello; exit 3"` · S2 `sh -c "echo hello stdout"` · S3 `sh -c "echo hello; echo world >&2"`
  - S4 `python3 -c "import os; os.write(1, b\"A\"*1048576); os.write(2, b\"B\"*1048576)"` — the image has python3, no python
  - S5 `sh -c 'printf "%d\n" $#; printf "%s\n" "$@"' sh <arg>` → stdout `1\na b "c" $d` (count line, then argument line)
  - S6 create the directory in the Given; run `sh -c "pwd"` with WorkingDirectory (pwd is a shell builtin, so run it via sh)
  - S7-S9 `sh -c "echo $VAR"` with the extra variables in the environment dictionary; "inherited" via Environment.SetEnvironmentVariable on the test process (S8/S9 set HOME to the same value, so no parallel-test race)
  - S10 `sh -c "sleep 10 & echo $! > <pidfile>; wait"` (pidfile unique per scenario, e.g. Guid-named in Path.GetTempPath()), timeout TimeSpan.FromSeconds(1)
  - "the program is stopped" = read the pidfile, assert the pid is dead: Process.GetProcessById throws ArgumentException, or the returned process has WaitForExit(0) true (a dead, reaped pid throws ArgumentException — verified)
  - S11 program name `factory-nonexistent-program-28da2742`; assert the message contains it
  - Step expressions (verified to bind with no ambiguity): plain literals where the text is plain; regex (parens) for backticks: `the argument `(.*)``, `the working directory is `(.*)``, `I run `(.*)` with CommandRunner`, `the CommandResult standard output is `(.*)``, `the program saw the argument `(.*)` character for character`, `the exception message names the program `(.*)``, `the exception message names the timeout `(.*)``; and regex for the bare env name: `the extra environment variable (\S+) as "([^"]*)"`, `the inherited environment variable (\S+) as "([^"]*)"`, `I run the program that prints its (\S+) environment variable`. Quoted values are {string}, byte counts {int}. Trim the trailing newline echo adds before comparing.
- tests/Specs/Unit/CommandRunnerTests.cs (new): xUnit class (name ends in Tests) for the sad paths below; it launches real sh (on PATH), as the binding does.

## Test Scenarios (unit-test behaviours, one line per sad path the pinned feature names)

- A program that exits non-zero: RunAsync returns a CommandResult whose ExitCode is 3 with no exception — a failed program is data, not a fault (feature line 6).
- A program that outlives the timeout: RunAsync with a one-second timeout throws a TimeoutException whose message names the program and contains "1 second", and the whole process tree is stopped — the sleeper's pid (written to a pidfile) is dead afterwards (feature lines 58-61).
- A program on no PATH: RunAsync throws an exception whose message contains the exact program name (feature lines 66-67).

## Soundness

Computed Thens, re-run in this sandbox (feature file specs/features/command-runner.feature):
- :6 — `sh -c 'echo hello; exit 3'; echo $?` → `3` — holds
- :11 — `sh -c 'echo hello stdout'` → `hello stdout` — holds
- :16, :17 — `sh -c 'echo hello; echo world >&2'`, streams separated → stdout=`hello`, stderr=`world` — holds
- :24 — the # command at line 20, run as `python3 -c 'import os; os.write(1, b"A"*1048576); os.write(2, b"B"*1048576)'` → stdout 1048576 bytes, every byte A — holds
- :25 — same command → stderr 1048576 bytes, every byte B — holds
- :30, :31 — `python3 - 'a b "c" $d'` (print argv count and exact equality) → count 1, exact True — holds
- :36 — `mkdir -p /tmp/command-runner-workdir-28da2742 && sh -c 'cd /tmp/command-runner-workdir-28da2742 && pwd'` → `/tmp/command-runner-workdir-28da2742` — holds
- :41 — `FACTORY_EXTRA=extra-value sh -c 'echo "$FACTORY_EXTRA"'` → `extra-value` — holds
- :47 — `HOME=inherited-home env HOME=extra-home sh -c 'echo "$HOME"'` → `extra-home` — holds
- :53 — `HOME=inherited-home sh -c 'echo "$HOME"'` → `inherited-home` — holds
- :58-61, :66-67 — no computed step (exception type, message content and the stop are properties of the not-yet-existing runner; the Thens follow from the scenario's own wording).

## Verify

- `dotnet build --no-restore` — every warning is an error; the CA rules below bite.
- `dotnet test --no-restore` — expect 12 feature tests (the 11 scenarios plus the greeting) plus the new unit tests, all green.
- `dotnet format --verify-no-changes --no-restore`.

## Notes for the builder

- CA1822: a step method that touches no instance state must be static; CA1805: do not initialise a field to its default (= null); CA1305: int.Parse needs CultureInfo.InvariantCulture. All three fired in the trial build.
- The generated feature class is Specs.Features.CommandRunnerRunsAProgramWithATimeoutAndReturnsItsExitCodeAndOutputFeature with method names from the scenario titles; reqnroll.json disables friendly display names (the title rides in the xUnit Description trait).
- No integration tests: the scenarios use only local sh and python3 (on PATH in the image); no GitHub, docker or Claude Code canned answers are involved.
- The trial left no residue: after the timeout kill, both sh and its sleep child were gone from /proc.
