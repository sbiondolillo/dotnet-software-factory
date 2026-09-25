# Plan: CommandRunner runs a program with a timeout and returns its exit code and output

`specs/features/command_runner.feature` (pinned; 11 scenarios) specifies `Core.CommandRunner.RunAsync`:
start a program with its arguments, an optional working directory, extra environment variables and an
optional timeout; return a `CommandResult` holding the exit code, the standard output and the standard error.
The scenarios run real programs (`sh`, `pwd`, `python3`) in process — no canned answers anywhere.

## Files to touch

- `src/Core/CommandResult.cs` (new): `public sealed class CommandResult` with `int ExitCode`,
  `string StandardOutput`, `string StandardError` (init properties, one constructor).
- `src/Core/CommandRunner.cs` (new): `public class CommandRunner` (concrete, not sealed) with
  `public virtual Task<CommandResult> RunAsync(string program, IReadOnlyList<string> arguments,
  string? workingDirectory = null, IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null,
  TimeSpan? timeout = null)`:
  - Build a `ProcessStartInfo` with `ArgumentList` (a list, never a shell string — the process is started
    directly, never wrapped in `sh -c`, so an argument containing spaces, `"` or `$` arrives character for
    character), `UseShellExecute = false`, `RedirectStandardOutput/Error = true`; set `WorkingDirectory`
    when given; add each `extraEnvironmentVariables` entry to `ProcessStartInfo.Environment` after the
    inherited set, so an extra name replaces an inherited variable of the same name, and an inherited name
    absent from the extras stays visible with its inherited value.
  - Read both streams concurrently (`BeginOutputReadLine` / `BeginErrorReadLine`, appending the lines
    under a lock) so 1 MB on each stream cannot deadlock; join the lines with a newline.
  - Await exit, honouring `timeout` via `WaitForExitAsync(timeout)`. When the timeout elapses first: kill
    the whole process tree and throw `System.TimeoutException` whose `Message` contains the program name
    and the timeout in the form `{milliseconds} milliseconds` (e.g. `Program 'sh' timed out after 1000
    milliseconds` for a 1000 ms timeout).
  - When `Process.Start` fails because the program is on no PATH: throw `InvalidOperationException` whose
    `Message` names the program (e.g. `Could not start program 'no-such-command-cd66d78d': ...`) with the
    original exception attached. Any exception type is acceptable; the message must contain the program name.
  - Return the captured streams with trailing line endings trimmed (`TrimEnd('\r', '\n')`), so `echo hello
    world` yields `hello world` and a program that writes exactly 1048576 characters with no trailing
    newline still returns a 1048576-character stream. Document the trimming in the XML doc.
- `tests/Specs/Steps/CommandRunnerSteps.cs` (new): `[Binding]` class `CommandRunnerSteps` in
  `namespace Specs.Steps;` (file-scoped), one instance per scenario, fields `_program`, `_arguments`,
  `_workingDirectory`, `_extraEnvironment`, `_timeout`, `CommandResult? _result`, `Exception? _failure`.
  Every When step calls `new CommandRunner().RunAsync(...)` in a try/catch, storing the result or the
  exception. Nullable is on and warnings are errors: a Then reading `_result` or `_failure` asserts
  non-null first. Step expressions (each matches the whole step text; `And` steps take the keyword above):
  - `[Given("the inherited environment variable {string} is {string}")]` → `Environment.SetEnvironmentVariable(name, value)` on the test process (feature:56, 63; the variables SHADOWED and KEPT are disjoint, so no scenario interferes with another).
  - `[When("I run {string}")]` (feature:72)
  - `[When("I run {string} with the arguments {string} and {string}")]` (features:9, 15, 21, 30)
  - `[When("I run {string} with the arguments {string} and {string} and {string} and {string}")]` (feature:38; `'a b "c" $d'` is a single-quoted `{string}` and must arrive as ONE argument)
  - `[When("I run {string} with the arguments {string} and {string} and the extra environment variable {string} is {string}")]` (features:50, 57, 64)
  - `[When("I run {string} in the working directory {string}")]` (feature:44; `Directory.CreateDirectory(path)` first — `/tmp/factory-cwd` does not exist in the image)
  - `[When("I run {string} with the arguments {string} and {string} with a timeout of {int} milliseconds")]` (feature:68; `TimeSpan.FromMilliseconds`)
  - `[Then("the CommandResult exit code is {int}")]` (features:10, 31) — `Assert.Equal(expected, _result.ExitCode)`
  - `[Then("the standard output is {string}")]` (features:16, 39, 45, 51, 58, 65) — `Assert.Equal(expected, _result.StandardOutput)`
  - `[Then("the standard error is {string}")]` (feature:22) — `Assert.Equal(expected, _result.StandardError)`
  - `[Then("the standard output is empty")]` (feature:23) — `Assert.Equal("", _result.StandardOutput)`
  - `[Then("the standard output holds {int} characters")]` (feature:32) and `[Then("the standard error holds {int} characters")]` (feature:33) — `Assert.Equal(expected, _result.StandardOutput.Length)` / `_result.StandardError.Length`
  - `[Then("RunAsync throws a TimeoutException whose message names {string} and {string}")]` (feature:69) — assert `_failure is TimeoutException` and its `Message` contains both strings; do not re-invoke RunAsync
  - `[Then("RunAsync throws an exception whose message names {string}")]` (feature:73) — assert `_failure` is non-null and its `Message` contains the string
- `tests/Specs/Unit/CommandRunnerTests.cs` (new): xUnit class `CommandRunnerTests` (name ends `Tests`; the
  binding gate reads only `*Feature` classes as Reqnroll's) with `[Fact]` methods calling
  `new CommandRunner().RunAsync(...)` directly, covering the Test Scenarios below.

## Test Scenarios

- A program that exits with code 3 returns a `CommandResult` whose `ExitCode` is 3 and does not throw (scenario "A program that exits with code 3 ...").
- Text a program writes to standard error lands in `StandardError` and none of it in `StandardOutput` (scenario "The standard error holds all the text ...").
- A program still running when the timeout passes is stopped and `RunAsync` throws a `System.TimeoutException` whose message names the program and the timeout in milliseconds (scenario "A program that still runs when the timeout passes ..."; the unit test may use a shorter timeout than the scenario's 1000 ms to keep the suite fast).
- A program that is on no PATH makes `RunAsync` throw an exception whose message names the program (scenario "A program that is on no PATH ..."; use a name that cannot exist, e.g. `no-such-command-cd66d78d`).

## Integration suite

No change: every scenario runs the real `sh`, `pwd` and `python3` in process through the real
`CommandRunner`; no canned answer (stub handler or Moq canned result) is introduced, so
`tests/Integration/` gains no test for this feature.

## Verify

- `dotnet test --no-restore` from the repo root: all 11 `CommandRunnerFeature` scenarios pass, plus
  `GreetingFeature` and the new unit tests.
- `dotnet build --no-restore` reports 0 warnings (warnings are errors); `dotnet format --verify-no-changes --no-restore` passes.

## Soundness

Computed in the sandbox; the `#` comment above each step is the command re-run (line = the Then it supports).

- specs/features/command_runner.feature:10 — `sh -c 'exit 3'; echo $?` → `3` — holds
- specs/features/command_runner.feature:16 — `sh -c 'echo hello world'` → `hello world` — holds
- specs/features/command_runner.feature:22 — `sh -c 'echo oops >&2' 2>&1` → `oops` — holds
- specs/features/command_runner.feature:23 — `sh -c 'echo oops >&2' 2>/dev/null | wc -c` → `0` (stdout empty) — holds
- specs/features/command_runner.feature:32 — `python3 -c "import sys; sys.stdout.write('a' * 1048576); sys.stderr.write('b' * 1048576)" 2>/dev/null | wc -c` → `1048576` — holds
- specs/features/command_runner.feature:33 — same command with `2>&1 1>/dev/null | wc -c` → `1048576` — holds
- specs/features/command_runner.feature:39 — `sh -c 'echo $# $1' _ 'a b "c" $d'` → `1 a b "c" $d` — holds
- specs/features/command_runner.feature:45 — `mkdir -p /tmp/factory-cwd && cd /tmp/factory-cwd && pwd` → `/tmp/factory-cwd` — holds
- specs/features/command_runner.feature:51 — `EXTRA_ALPHA=alpha sh -c 'echo $EXTRA_ALPHA'` → `alpha` — holds
- specs/features/command_runner.feature:58 — `SHADOWED=new sh -c 'echo $SHADOWED'` → `new` — holds
- specs/features/command_runner.feature:65 — `KEPT=kept sh -c 'echo $KEPT'` → `kept` — holds
- specs/features/command_runner.feature:69 (timeout scenario) — no computed step (no `#` comment); precondition holds: `sleep 30` outlives a 1000 ms timeout
- specs/features/command_runner.feature:73 (no-PATH scenario) — no computed step; precondition verified: `command -v no-such-command-cd66d78d` → exit 1 (on no PATH)
- specs/features/greeting.feature — no computed step
