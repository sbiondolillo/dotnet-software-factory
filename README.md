# dotnet-software-factory

A csharp workpiece for homelab-factory. It started from the factory's
`csharp` pattern, and it passed the factory's readiness check before its
first commit. What each file is:

| file | what it is |
|---|---|
| `docs/agents/setup-sheet.toml` | the factory's setup sheet: the toolchain and the directories only this repo can name |
| `docs/agents/layout.md` | the directory rules and how a scenario reaches the code, for an agent |
| `AGENTS.md` | the pointer an agent reads first |
| `Solution.slnx` | the solution: every `dotnet` command at the root reads it |
| `Directory.Packages.props` | the manifest: every package version, under central package management |
| `packages.lock.json` | one per project, the resolved graph that `dotnet restore --locked-mode` holds to |
| `Directory.Build.props`, `.editorconfig`, `global.json` | the build settings, the code style and the SDK pin. The factory compares them against the pin |
| `src/Core/` | the library, with `Greeter` |
| `src/App/` | the console program, built as `dotnet-software-factory` |
| `.github/workflows/factory.yml` | a `factory` label on an issue starts a homelab-factory run on it |
| `.github/workflows/ci.yml` | the checks: the `build` job, then the `integration` job |
| `specs/features/` | the pinned `.feature` files. One ships here, so the first build has a binding to follow |
| `tests/Specs/` | the test project: Reqnroll under xUnit v3, with the step class for that feature |
| `tests/Integration/` | the integration suite: xUnit v3 tests against the real GitHub, `docker` and Claude Code. `dotnet test` skips it |

## Commands

```
dotnet restore --locked-mode             # the packages, from the locks
dotnet build --no-restore                # build; every warning is an error
dotnet format --verify-no-changes        # lint
dotnet test --no-restore                 # the test suite, no network needed
dotnet run --project tests/Integration   # the integration suite, network needed
dotnet run --project src/App -- Ada      # the console program
```
