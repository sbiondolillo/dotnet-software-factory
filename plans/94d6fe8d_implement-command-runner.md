# Plan — CommandRunner (run 94d6fe8d)

Implement `CommandRunner` in `src/Core`, bind every one of the 11 scenarios in the
pinned `specs/features/command-runner.feature`, and add unit tests. `specs/` is
frozen (the builder must not edit it). `app_docs/` is the documenter's namespace:
no writes there.

## Files

- `src/Core/CommandResult.cs` (new) — `namespace Core;` (file-scoped);
  `public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);`.
- `src/Core/CommandRunner.cs` (new) — concrete, non-sealed `public class CommandRunner` with a
  parameterless ctor and `public virtual Task<CommandResult> RunAsync(string program,
  IEnumerable<string> arguments, string? workingDirectory = null,
  IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null, TimeSpan? timeout = null)`
  (the acceptance text requires the class to be concrete and `RunAsync` virtual, for Moq):
  - build `ProcessStartInfo` with `UseShellExecute = false`, both streams redirected, and every
    argument added to `ArgumentList` (no shell: lines 48-51 pin character-for-character arguments);
  - `WorkingDirectory` when given; extra variables set on `psi.Environment`. Verify in place that
    `psi.Environment` overrides inherited same-named variables and preserves the rest (lines 80, 91
    depend on it); if the SDK disagrees, build the full environment from
    `Environment.GetEnvironmentVariables()` plus the extras;
  - read stdout and stderr concurrently (two `ReadToEndAsync` under `Task.WhenAll`): the lines
    34-44 scenario fills both 64 KB pipe buffers and would deadlock on sequential reads;
  - no timeout: wait for exit and return `new CommandResult(process.ExitCode, stdout, stderr)`;
  - timeout: `WaitForExitAsync` with a linked cancellation token; on cancellation kill the process
    (entire tree, tolerate already-exited) and throw `System.TimeoutException` whose message contains
    the program name and the timeout, formatting whole-second timeouts as `N seconds` (lines 97-99
    pin the literal `sleep` and `2 seconds`);
  - start failure (lines 102-105): catch the start exception and throw an exception whose message
    contains the program name (e.g. `InvalidOperationException`); never spawn a shell for it.
- `tests/Specs/Steps/CommandRunnerSteps.cs` (new) — `[Binding]` class `CommandRunnerSteps` in
  `namespace Specs.Steps;`; Reqnroll makes one instance per scenario; fields hold the program,
  arguments, working directory, extra env, the `CommandResult?`, the caught `Exception?`, and the
  `sleep`-PID snapshot:
  - `[Given("the program is {string}")]` — store the program name.
  - `[Given("its arguments are (.+)")]` — parse the quoted spans (`"..."` or `'...'`) separated by
    ` and ` into a list; line 48 mixes both quote styles in one step and no pinned span contains an
    escaped quote, so a span ends only at its own quote character; throw on malformed input.
  - `[Given("the working directory is {string}")]` — store it and `Directory.CreateDirectory`
    (the directory must exist for `pwd`, lines 54-59).
  - `[Given("the inherited environment variable {string} is {string}")]` —
    `Environment.SetEnvironmentVariable` so the test process, and its children, see it (lines 72, 83).
  - `[Given("the extra environment variables are")]` with `DataTable` — name/value rows into a
    dictionary (lines 62-64, 73-75, 84-86).
  - `[When("I run the program")]` / `[When("I run the program with a timeout of {int} seconds")]` —
    snapshot the PIDs of live processes named `sleep`, `await new CommandRunner().RunAsync(...)`,
    storing the result or catching the exception into the field.
  - `[Then("RunAsync returns without throwing")]` — the exception field is null.
  - `[Then("the exit code is {int}")]` — `Assert.Equal` on `result.ExitCode` (lines 14, 40).
  - `[Then("the standard output is {string}")]` — exact `Assert.Equal`; the `printf %s` programs
    emit no trailing newline (Soundness below).
  - `[Then("the standard output contains {string}")]` / `[Then("the standard output does not
    contain {string}")]` / `[Then("the standard error contains {string}")]` — `Assert.Contains` /
    `Assert.DoesNotContain` on the stored streams (lines 21, 28, 30, 32, 59).
  - `[Then("the standard output is {int} characters long")]` / `[Then("the standard error is
    {int} characters long")]` — `Assert.Equal` on `.Length` (lines 42, 44).
  - `[Then("RunAsync throws a TimeoutException")]` — `Assert.IsType<TimeoutException>`;
    `[Then("the exception message contains {string}")]` (lines 98-99) and `[Then("RunAsync throws
    an exception whose message contains {string}")]` (line 105) — `Assert.Contains` on the caught
    message.
  - `[Then("the program is no longer running")]` (line 100) — every live `sleep` PID was already in
    the When-step snapshot.
- `tests/Specs/Unit/CommandRunnerTests.cs` (new) — xUnit class `CommandRunnerTests` in
  `namespace Specs.Unit;` (name ends `Tests`, not `Feature`); covers the behaviours in Test
  Scenarios below with the real `CommandRunner` against local `sh`/`sleep`, plus one Moq test of
  the virtual `RunAsync` (the acceptance text: caller code is tested by mocking `RunAsync`).
- `tests/Specs/XunitAssemblyInfo.cs` (new) — `[assembly: Xunit.CollectionBehavior(
  DisableTestParallelization = true)]`: xUnit v3 runs test classes in parallel by default, and a
  concurrently running unit-test `sleep` would pollute the line-100 PID-snapshot check.
- No csproj or package changes: `System.Diagnostics.Process` is in the shared framework, and
  `Specs.csproj` already references `Core.csproj` and Moq. `tests/Integration/` is untouched: the
  scenarios use local binaries, no canned answers, so no integration tests are needed.

## Verify

From the repo root: `dotnet build --no-restore`, `dotnet format --verify-no-changes --no-restore`,
`dotnet test --no-restore` — the 11 command-runner scenarios, the greeting scenario, and the new
unit tests all pass. Warnings are errors; file-scoped namespaces are required.

## Test Scenarios

- Timeout: a program still running when the timeout passes is stopped, `RunAsync` throws
  `System.TimeoutException` whose message names the program and the timeout, and no spawned
  process is left running.
- Not on PATH: `RunAsync` for a program on no PATH throws an exception whose message contains
  the program name.
- Stream separation: text a program writes to stderr never appears in
  `CommandResult.StandardOutput`, and text written to stdout never appears in `StandardError`.
- 1 MB output: a program that writes 1 MB to each stream completes and `RunAsync` returns without
  throwing, both outputs whole.
- Mockable: `RunAsync` is `virtual`, so a Moq `Mock<CommandRunner>` hands a canned
  `CommandResult` to the caller.

## Soundness

Every command below ran in the sandbox with the scenario's exact inputs; `holds`/`fails` per line.

- command-runner.feature:14 — `sh -c 'exit 3'; echo $?` -> `3` — holds
- command-runner.feature:21 — `sh -c 'echo all of this text goes to standard output'` -> `all of this text goes to standard output` — holds
- command-runner.feature:28 — `sh -c 'echo the stdout line; echo the stderr line >&2' 2>&1 1>/dev/null` -> `the stderr line` — holds
- command-runner.feature:30 — `sh -c 'echo the stdout line; echo the stderr line >&2' 2>/dev/null` -> `the stdout line` — holds
- command-runner.feature:32 — same command, searched for `the stderr line` -> `absent` — holds
- command-runner.feature:38,40 — `python3 -c "import sys; sys.stdout.write('A' * 1048576); sys.stderr.write('E' * 1048576)" >/dev/null 2>&1; echo $?` -> `0` — holds
- command-runner.feature:42 — same python command, `2>/dev/null | wc -c` -> `1048576` — holds
- command-runner.feature:44 — same python command, `2>&1 >/dev/null | wc -c` -> `1048576` — holds
- command-runner.feature:51 — `sh -c 'printf %s "$1"' _ 'a "quoted" $VAR space'` -> `a "quoted" $VAR space`, 21 bytes, no trailing newline — holds
- command-runner.feature:59 — `mkdir -p /tmp/factory-cwd-check; (cd /tmp/factory-cwd-check && sh -c 'pwd')` -> `/tmp/factory-cwd-check` — holds
- command-runner.feature:69 — `FACTORY_CHECK_VALUE='hello world 42' sh -c 'printf %s "$FACTORY_CHECK_VALUE"'` -> `hello world 42`, 14 bytes — holds
- command-runner.feature:80 — `FACTORY_OVERRIDE_CHECK='the extra value' sh -c 'printf %s "$FACTORY_OVERRIDE_CHECK"'` -> `the extra value`, 15 bytes — holds
- command-runner.feature:91 — `FACTORY_INHERITED_CHECK='the inherited value' sh -c 'printf %s "$FACTORY_INHERITED_CHECK"'` -> `the inherited value`, 19 bytes — holds
- command-runner.feature:97,100 — `timeout 2 sleep 5; echo $?` -> `124` (sleep still running at 2 s, then stopped by timeout) — holds
- command-runner.feature:98,99 — `python3 -c 'given={"program":"sleep","timeout":"2 seconds"}; need=["sleep","2 seconds"]; print(all(n in given.values() for n in need))'` -> `True` (the message must contain the Given's own program and timeout, which the step text supplies verbatim) — holds
- command-runner.feature:105 — `sh -c 'factory-not-a-real-program'; echo $?` -> `127`; `command -v factory-not-a-real-program` -> exit `1` (on no PATH) — holds
- greeting.feature:5 — `python3 -c 'name="Ada"; print("Hello, "+name+"!")'` -> `Hello, Ada!` (mirrors `src/Core/Greeter.cs:7`) — holds
