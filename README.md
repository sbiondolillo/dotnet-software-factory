# dotnet-software-factory

A csharp workpiece for homelab-factory. It started from the factory's
`csharp` pattern, and it passed the factory's readiness check before its
first commit. What each file is:

| file | what it is |
|---|---|
| `docs/agents/setup-sheet.toml` | the factory's setup sheet: the toolchain and the directories only this repo can name |
| `AGENTS.md` | the directory rules and how a scenario reaches the code, for an agent. An agent reads it first |
| `Solution.slnx` | the solution: every `dotnet` command at the root reads it |
| `Directory.Packages.props` | the manifest: every package version, under central package management |
| `packages.lock.json` | one per project, the resolved graph that `dotnet restore --locked-mode` holds to |
| `Directory.Build.props`, `.editorconfig`, `global.json` | the build settings, the code style and the SDK pin. The factory compares them against the pin |
| `src/Core/` | the library, with `Greeter` |
| `src/App/` | the console program, built as `dotnet-software-factory` |
| `.github/workflows/ci.yml` | the checks: the `build` job, then the `integration` job |
| `.github/workflows/relock.yml` | on a Dependabot pull request, regenerates the `packages.lock.json` of each project in `Solution.slnx` and pushes them with the Dependabot secret `RELOCK_TOKEN` |
| `specs/features/` | the pinned `.feature` files. One ships here, so the first build has a binding to follow |
| `tests/Specs/` | the test project: Reqnroll under xUnit v3, with the step class for that feature |
| `tests/Integration/` | the integration suite: xUnit v3 tests against the real GitHub, `docker` and Claude Code. `dotnet test` skips it |

## Start a new repo

Make a new repo from the starter template:

```
dotnet new install <checkout>/templates/starter
dotnet new factory-starter -o <directory>
```

Push the copy to the branch `main` of a new GitHub repo.

The repo's owner starts a trial run of the factory at another ref with the command `gh workflow run factory -f issue=<number> -f ref=<ref>`. The ref is a branch, a tag or a commit.

Add two Actions secrets to the new repo: `FACTORY_GITHUB_TOKEN` and `ANTHROPIC_API_KEY`.

`FACTORY_GITHUB_TOKEN` is a fine-grained token with read and write access to Contents, Issues and Pull requests on the new repo, and read access to Contents on `sbiondolillo/dotnet-software-factory`.

`factory.yml` and `review-feedback.yml` pass the job's own token to the factory as `FACTORY_CHECKS_TOKEN`, with the permission `checks: read`. The factory reads the check runs of each pull request with it, on a public repo and on a private repo alike. A fine-grained token has no Checks permission, so `FACTORY_GITHUB_TOKEN` reads check runs on a public repo only.

Replace `EXAMPLE_API_KEY` in `factory.yml`, in `review-feedback.yml` and in `ci.yml` with the name of each secret the integration tests of the new repo read.

Protect the branch `main`, with the checks `build` and `integration` required.

The factory requests a Copilot code review of each pull request it watches, and waits for that review on the head commit. This needs a Copilot plan that includes Copilot code review. With no such plan, add `--copilot-review off` to the run command in `factory.yml` and in `review-feedback.yml`. The factory then requests no review and waits for the checks alone. The review prompt of the factory reviews each build with either value.

The repo's owner starts a run with the label `factory` on an issue. `factory.yml` names the tag `v0` of the factory.

The body of an issue can hold its scenarios under the heading `## Scenarios`, and the factory then pins them as the owner wrote them. Each fenced block under the heading starts with the line `# specs/features/<name>.feature`. A whole-file block holds a `Feature:` line, and it becomes the file or replaces the file on `main`. Any other block is an upsert into the file on `main`. A scenario whose name the file holds replaces that scenario in place, and a scenario with a new name goes at the end of the file. Under the heading `## Removed scenarios`, each list item names one scenario to remove, as `` `specs/features/<name>.feature`: <scenario name> ``. The plan phase then writes only `.factory/plan.md`. A body whose scenarios are not sound gets a comment that names each fault, and no run.

Create the label `review-feedback` in the new repo. The repo's owner starts a feedback run with the label `review-feedback` on a pull request. The run removes the label, so the owner applies it again for the next round.

Create the label `cleanup` in the new repo. The repo's owner starts a cleanup run, a run with no plan, with the label `cleanup` on an issue that fixes a test, a config file or a document. The label `cleanup` and the label `behavior` on one issue get a comment and no run.

The body of a `cleanup` issue names the paths the build can change under the heading `## Scope`, one for each line. A line is a path, either bare or as a Markdown list item with the marker `-`, `*` or `+`, and the path can sit in backticks. A directory ends in `/`, and a path with `*` is a glob. The body holds under the heading `## Check` one fenced `sh` block, the check command, that fails before the change and exits with the code 0 after it. The factory runs the command with `sh -c` in a copy of the work tree, and a command that runs `dotnet` restores its packages itself first.

A feedback run on the pull request of a `cleanup` issue holds the build to the issue's `## Scope` and runs its `## Check` after the tests pass.

Create the label `refactor` in the new repo. The repo's owner starts a refactor run with the label `refactor` on an issue that changes the structure of the code and adds no behaviour. The plan of a refactor run writes no feature file, and the build leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is. The body can hold under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it, and the factory runs it as it runs the check of a `cleanup` issue. The label `refactor` with the label `behavior` or the label `cleanup` on one issue gets a comment and no run.

A feedback run on the pull request of a `refactor` issue leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is, and runs the issue's `## Check` after the tests pass.

Create the label `reframe` in the new repo. The repo's owner starts a reframe run, a run with no plan, with the label `reframe` on an issue. A reframe issue keeps the behaviour of the program and changes the code, the tests or the config. A format sweep and a framework upgrade are reframe issues. The body names the paths the build can change under the heading `## Scope`, as a `cleanup` issue does. The scope can name `src/`. The body holds under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it. The run records the tests that pass on the base commit. It also records the public API of the code under `src/`: one line per public type and per public or protected member. The build runs again when it loses a test or changes a line of that API. The label `reframe` with the label `behavior`, `cleanup` or `refactor` on one issue gets a comment and no run.

## Commands

```
dotnet restore --locked-mode             # the packages, from the locks
dotnet build --no-restore                # build; every warning is an error
dotnet format --verify-no-changes        # lint
dotnet test --no-restore                 # the test suite, no network needed
dotnet run --project tests/Integration   # the integration suite, network needed
dotnet run --project src/App -- Ada      # the console program
```
