# Does the factory build a workpiece with DSF's own layout

Research for issue #274. Read off `src/Core/` at commit `ecd0afb`. Each line of the question gets a verdict, the class and the line that decides it, and the evidence. A fail names the smallest DSF change that makes it work.

## Summary

The factory clones, plans, builds, lints, tests and delivers DSF's own layout without a path assumption that DSF breaks. The layout check, the pin, the scenario check and the sandbox image all work on DSF. One step fails on every run kind: the test gate ends with `dotnet run --project tests/Integration` inside the sandbox, and DSF's integration suite needs `docker`, which the sandbox image does not hold. The smallest change is a `--skip-integration` option that skips that one command. CI's integration job still runs the suite on the pull request, and the watch reads its check run.

## The clone and the branch

Verdict: works.

- `Git.CloneAsync`, `src/Core/Git.cs` lines 7 to 17, runs `git clone <url> <directory>`. The directory is a fresh temp directory, `ConsoleCommands.cs` line 174. No path of the starter is named.
- `FactoryRun`, `src/Core/FactoryRun.cs` lines 41 to 42 and 134 to 143, checks the clone for `Solution.slnx`, `specs/features/`, `tests/Specs/` and `tests/Integration/`. DSF holds all four.
- `Git.NestedGitEntries`, `Git.cs` lines 189 to 232, fails every git command when the work tree holds a second `.git`. A clone of DSF holds none: `templates/starter/` has no `.git`, and `.claude/worktrees/` is not tracked.
- `Deliver.RunAsync`, `src/Core/Deliver.cs` lines 24 to 26, creates `factory/<runId>` and pushes it. The base branch comes from the repo's `default_branch`, `ConsoleCommands.cs` lines 166 to 167. No path.
- `PlanPhase.Snapshot`, `src/Core/PlanPhase.cs` lines 185 to 207, hashes every file outside `.git/`, `bin/`, `obj/` and `.factory/`. On DSF this includes `templates/starter/**` and `docs/**`. That costs time, not correctness.
- `Sandbox.StartAsync`, `src/Core/Sandbox.cs` lines 30 to 38, mounts the clone at `/work` and its `.git` read-only. No path of the starter.
- Nothing in `src/Core/` reads the clone's `templates/`, `sandbox/` or `docs/`.

## TestGate and the other gates

Verdict: fails, at the integration run alone.

What each command does on DSF's solution, in the order the gate runs them:

| command | class and line | on DSF |
|---|---|---|
| `dotnet format --no-restore` | `LintGate.cs` line 16 | finds `Solution.slnx`, formats all four projects. Works. CI runs the same. |
| `dotnet restore --locked-mode` | `TestGate.cs` line 44 | every project has `packages.lock.json`. Works. |
| `dotnet build --no-restore` | `TestGate.cs` line 45 | builds all four projects, `tests/Integration` included. Works. |
| `dotnet format --verify-no-changes --no-restore` | `TestGate.cs` line 46 | works, the lint gate ran first. |
| `dotnet test --no-restore --logger trx --results-directory /work/.test-gate-<guid>` | `TestGate.cs` lines 62 to 65 | collects `tests/Specs`. Skips `tests/Integration`: `Integration.csproj` line 10 sets `IsTestProject` false. Works. CI line 46 runs the same. |
| skipped tests fail the gate | `TestGate.cs` lines 90 to 93 | DSF's one conditional skip, `GitTests.cs` line 294, fires only when the process is root. The sandbox runs as the host uid, `Sandbox.cs` lines 29 and 50. On a GitHub runner that is 1001. Works. A factory run as root fails here. |
| scenario coverage | `TestGate.cs` lines 96 to 106 | reads `specs/features/` at any depth and matches TRX method names. `tests/Specs/reqnroll.json` sets `disableFriendlyTestNames` true, as the starter does. Works. |
| `dotnet run --no-restore --project tests/Integration` | `TestGate.cs` lines 121 to 127 | fails. See below. |

The integration run fails on DSF for two reasons:

1. The suite runs `docker`: `SandboxIntegrationTests.cs` line 27, `SandboxImageIntegrationTests.cs` line 13, and every test that uses the tag `factory-sandbox:integration`. The sandbox image installs no docker client, `sandbox/Dockerfile` lines 5 to 14, and `Sandbox.StartAsync` mounts no socket, `Sandbox.cs` lines 41 to 60. The first such test fails, the process exits non-zero, and the gate reports `dotnet run exited with the code ...`.
2. Sixteen test files read `FACTORY_GITHUB_TOKEN` and target `sbiondolillo/factory-scratch`. The variable reaches the suite only through `--pass-env`. See the last section.

Every run kind reaches this line: `FactoryRun.cs` lines 218, 329, 445 and 564, and `FeedbackRun.cs` lines 121 to 123. So no DSF run passes the test gate today.

Smallest DSF change: an option `--skip-integration` on `run` and `feedback` in `ConsoleCommands.cs`, a `bool SkipIntegration` on `FactoryRunRequest` and `FeedbackRunRequest`, and one `if` before `TestGate.cs` line 121 that returns `new TestGateResult(true, [], tests)`. DSF's own factory workflow passes the option. CI's `integration` job still runs the suite on the pull request, and `PullRequestWatch` reads its check run, so the suite stays a gate before merge.

The other gates hold no path that DSF breaks. `ApiGate.ListAsync`, `ApiGate.cs` line 38, prints the API of `src`, which DSF has. `CleanupGate.RunCheckAsync`, `CleanupGate.cs` lines 46 to 66, runs the check in a detached worktree at `/tmp`, and `CleanupGate.ScopeFaultsAsync`, lines 8 to 30, judges paths against the issue's `## Scope`.

## SandboxImage

Verdict: works. The factory's own `sandbox/` is read, never the clone's.

- `SandboxImage.EnsureAsync`, `src/Core/SandboxImage.cs` lines 20 to 37, reads `global.json` and `sandbox/Dockerfile` under `rootDirectory`, and uses `rootDirectory` as the build context.
- `rootDirectory` is `host.RootDirectory`, which `src/App/Program.cs` line 23 sets to the factory process's current directory. `ConsoleCommands.cs` line 179 puts it in the request, `FactoryRun.cs` line 157 passes it to `Sandbox.StartAsync`, and `Sandbox.cs` line 27 passes it to `EnsureAsync`.
- The clone lives in a temp directory, `ConsoleCommands.cs` line 174, and no code reads `sandbox/` under it.
- The tag is fixed, `dotnet-software-factory-sandbox`, `ConsoleCommands.cs` line 18. An image with that tag is reused, `SandboxImage.cs` lines 12 to 18.

Two notes. The factory's current directory must be the DSF checkout root. The starter's `factory.yml` line 39 runs `dotnet run --project src/App` from that root, so it holds. DSF has no `.dockerignore`, so the first build sends the whole checkout as context. That is slow, not wrong.

## FeatureFiles and the `## Scenarios` pin

Verdict: works.

- `IssueScenarios`, `src/Core/IssueScenarios.cs` lines 16 to 17 and the regex at line 510, accepts `specs/features/<name>.feature` only, with no subdirectory. Every DSF feature file is flat under `specs/features/`.
- `IssueScenarios.Apply` writes the file under the clone, lines 163 to 168. `tests/Specs/Specs.csproj` line 21 links `../../specs/features/**/*.feature`, so a new file is compiled with no project edit.
- `TestGate.cs` line 96 reads `specs/features/` at any depth, and `FeatureFiles.ComputeTestName`, `FeatureFiles.cs` line 159, names the test as Reqnroll does. DSF's `reqnroll.json` matches the starter's.
- `Pin.IsPinned`, `src/Core/Pin.cs` lines 82 to 100, pins `specs/features/**` and every file with a settings name at any depth. On DSF that adds `templates/starter/.editorconfig`, `templates/starter/Directory.Build.props`, `templates/starter/global.json` and `templates/starter/tests/Specs/reqnroll.json` to the pin.

One limit from the last point. A behavior run that must change one of those four files fails the gate with `pin: templates/starter/...`, `TestGate.cs` lines 37 to 40. Such a change goes in a `reframe` or `cleanup` issue whose `## Scope` names the path: `CleanupGate.PinExempt`, `CleanupGate.cs` lines 32 to 33, exempts an in-scope path outside `specs/features/`. No DSF change is needed. An optional one is to pin a settings name only outside `templates/`.

## The `--pass-env` and the secrets the sandbox gets

Verdict: works. Nothing to pass for DSF once the integration run is skipped.

- `ConsoleCommands.cs` lines 57 to 61 read `--pass-env`, line 110 puts the names in `Options.SecretNames`, and line 180 puts them in `FactoryRunRequest.SecretNames`.
- `TestGate.cs` lines 121 to 123 give the names to the integration `dotnet run` alone. `Sandbox.RunAsync`, `Sandbox.cs` lines 92 to 96, adds `--env <name>` to `docker exec`, which copies the value from the factory process's own environment. No other command and no agent prompt gets them.
- `ANTHROPIC_API_KEY` is set on the container at `docker run`, `Sandbox.cs` line 55. Every command in the container holds it, the Specs suite included.

For DSF's integration suite the needed names are `ANTHROPIC_API_KEY`, already present, and `FACTORY_GITHUB_TOKEN`. Passing the second forwards the run's own token, the one with write access on DSF, into a suite the agent may edit: `Pin.cs` lines 91 to 93 pin `tests/Integration/` in a refactor run only. The suite also targets `factory-scratch`, and `ci.yml` line 12 says CI's token covers that repo alone. So a self-build never passes `FACTORY_GITHUB_TOKEN`. With `--skip-integration` there is nothing to pass.

## Verdicts

| bullet | verdict |
|---|---|
| The clone and the branch | works |
| TestGate and the other gates | fails at `dotnet run --project tests/Integration`: the sandbox has no docker |
| SandboxImage | works, the factory's own `sandbox/` is read |
| FeatureFiles and the `## Scenarios` pin | works, the pin also covers four files under `templates/starter/` |
| `--pass-env` and the secrets | works, pass nothing for DSF |

DSF changes needed:

1. `--skip-integration` on `run` and `feedback`, carried in the two request records, and one `if` before `TestGate.cs` line 121. DSF's own factory workflow passes it.
