# dotnet-software-factory

A software factory for C# repos. A repo made from its starter gets one pull request for each issue that its owner labels `factory`. Claude Code writes the change in a sandbox on the GitHub runner, and the owner reviews and merges each pull request. What each file of this repo is:

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
| `LICENSE` | the MIT license. The starter holds the same file, so a repo made from it is MIT too |

## Start a new repo

Make a new repo from the starter template:

```
dotnet new install <checkout>/templates/starter
dotnet new factory-starter -o <directory>
```

Push the copy to the branch `main` of a new GitHub repo.

The repo's owner starts a trial run of the factory at another ref with the command `gh workflow run factory -f issue=<number> -f ref=<ref>`. The ref is a branch, a tag or a commit.

Add two Actions secrets to the new repo: `FACTORY_GITHUB_TOKEN` and `ANTHROPIC_API_KEY`.

`FACTORY_GITHUB_TOKEN` is a fine-grained token. Make it after the new repo exists, because a fine-grained token can select only a repo that exists. Under Repository access, select the new repo alone, and give the token read and write access to Contents, Issues, Pull requests and Workflows. Workflows lets a run push a change under `.github/workflows/`.

`factory.yml` and `review-feedback.yml` pass the job's own token to the factory as `FACTORY_CHECKS_TOKEN`, with the permission `checks: read`. The factory reads the check runs of each pull request with it, on a public repo and on a private repo alike. A fine-grained token has no Checks permission, so `FACTORY_GITHUB_TOKEN` reads check runs on a public repo only.

Replace `EXAMPLE_API_KEY` in `factory.yml`, in `review-feedback.yml` and in `ci.yml` with the name of each secret the integration tests of the new repo read.

A repo whose integration tests read no secret keeps `EXAMPLE_API_KEY` as it is. The secret is not set, and each step gets an empty value.

Protect the branch `main`, with the checks `build` and `integration` required. Run this command in the checkout of the new repo, and `gh` fills in `{owner}` and `{repo}`:

```
gh api -X PUT repos/{owner}/{repo}/branches/main/protection --input - <<'JSON'
{"required_status_checks":{"strict":false,"contexts":["build","integration"]},"enforce_admins":false,"required_pull_request_reviews":null,"restrictions":null}
JSON
```

GitHub applies the protection on a public repo, and on a private repo of a paid plan. The command sets `enforce_admins` to `false`, so an admin of the repo, such as its owner, can still merge a pull request with red checks. See `## A private target repo`.

The factory requests a Copilot code review of each pull request it watches, and waits for that review on the head commit. This needs a Copilot plan that includes Copilot code review. With no such plan, add `--copilot-review off` to the run command in `factory.yml` and in `review-feedback.yml`. The factory then requests no review and waits for the checks alone. The review prompt of the factory reviews each build with either value.

Create the label `factory` in the new repo. A new GitHub repo has no label of that name.

The repo's owner starts a run with the label `factory` on an issue. `factory.yml` names the tag `v0` of the factory.

The body of an issue can hold its scenarios under the heading `## Scenarios`, and the factory then pins them as the owner wrote them. Each fenced block under the heading starts with the line `# specs/features/<name>.feature`. A whole-file block holds a `Feature:` line, and it becomes the file or replaces the file on `main`. Any other block is an upsert into the file on `main`. A scenario whose name the file holds replaces that scenario in place, and a scenario with a new name goes at the end of the file. Under the heading `## Removed scenarios`, each list item names one scenario to remove, as `` `specs/features/<name>.feature`: <scenario name> ``. The plan phase then writes only `.factory/plan.md`. A body whose scenarios are not sound gets a comment that names each fault, and no run.

Create the label `review-feedback` in the new repo. The repo's owner starts a feedback run with the label `review-feedback` on a pull request. The run removes the label, so the owner applies it again for the next round.

Create the label `cleanup` in the new repo. The repo's owner starts a cleanup run, a run with no plan, with the label `cleanup` on an issue that fixes a test, a config file or a document. The label `cleanup` and the label `behavior` on one issue get a comment and no run.

The body of a `cleanup` issue names the paths the build can change under the heading `## Scope`, one for each line. A line is a path, either bare or as a Markdown list item with the marker `-`, `*` or `+`, and the path can sit in backticks. A directory ends in `/`, and a path with `*` is a glob. The body holds under the heading `## Check` one fenced `sh` block, the check command, that fails before the change and exits with the code 0 after it. The factory runs the command with `sh -c` in a copy of the work tree, and a command that runs `dotnet` restores its packages itself first.

A feedback run on the pull request of a `cleanup` issue holds the build to the issue's `## Scope` and runs its `## Check` after the tests pass.

Create the label `refactor` in the new repo. The repo's owner starts a refactor run with the label `refactor` on an issue that changes the structure of the code and adds no behaviour. The plan of a refactor run writes no feature file, and the build leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is. The body can hold under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it, and the factory runs it as it runs the check of a `cleanup` issue. The label `refactor` with the label `behavior` or the label `cleanup` on one issue gets a comment and no run.

A feedback run on the pull request of a `refactor` issue leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is, and runs the issue's `## Check` after the tests pass.

Create the label `reframe` in the new repo. The repo's owner starts a reframe run, a run with no plan, with the label `reframe` on an issue. A reframe issue keeps the behaviour of the program and changes the code, the tests or the config. A format sweep and a framework upgrade are reframe issues. The body names the paths the build can change under the heading `## Scope`, as a `cleanup` issue does. The scope can name `src/`. The body holds under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it. The run records the tests that pass on the base commit. It also records the public API of the code under `src/`: one line per public type and per public or protected member. The build runs again when it loses a test or changes a line of that API. The label `reframe` with the label `behavior`, `cleanup` or `refactor` on one issue gets a comment and no run.

## A private target repo

The factory runs on a private repo, with these limits:

- GitHub enforces branch protection on a private repo only on a paid plan. On the free plan of a personal account, GitHub shows "Your rules won't be enforced on this private repository until you move to a GitHub Team or Enterprise organization account". The factory still waits for green checks, but GitHub lets each person with write access merge a pull request with red checks.
- `FACTORY_GITHUB_TOKEN` must select each target repo, public or private. A new repo needs a new token.
- An organization can require approval of each fine-grained token. Until an administrator approves it, the token reads only public resources.
- GitHub never shows a secret again after you save it. Keep each value in a store outside GitHub, such as a password manager.
- The job's own token reads the check runs, as `FACTORY_CHECKS_TOKEN` above. The repo's owner sets nothing for it.
- A Copilot code review on a private repo is not tested. The trial on a private repo ran with `--copilot-review off`.

## Run the demo

Four issues on the greeter show the four kinds of run. Each body is a file under `docs/demo/` of this repo. Start from a repo that `## Start a new repo` made, with its secrets set and no other change. Run each command below in the checkout of the new repo, signed in to `gh` as the repo's owner. `<checkout>` is the checkout of this repo.

| Step | Kind | Body | Label | What the pull request does |
|---|---|---|---|---|
| 1 | `behavior` | `docs/demo/1-behavior.md` | none | adds the option `--shout`, and three scenarios |
| 2 | `cleanup` | `docs/demo/2-cleanup.md` | `cleanup` | adds `README.md`, byte for byte with the text in the body |
| 3 | `refactor` | `docs/demo/3-refactor.md` | `refactor` | moves the upper-case rule into `Greeter`, and changes no test |
| 4 | `reframe` | `docs/demo/4-reframe.md` | `reframe` | requires an SPDX license header on each C# file, and adds it |

File the issues in this order. The second and the third issue need the option `--shout`, which the first adds. Merge each pull request before you file the next issue.

Create the labels. `--force` updates a label that exists:

```
for label in factory cleanup refactor reframe; do gh label create "$label" --force; done
```

### File each issue

Step 1:

```
n=$(gh issue create --title 'A person can print the greeting in upper case with the option `--shout`' --body-file <checkout>/docs/demo/1-behavior.md | sed 's|.*/||')
```

Step 2:

```
n=$(gh issue create --title 'The repo has a `README.md` that says what the program does and how to run and test it' --body-file <checkout>/docs/demo/2-cleanup.md --label cleanup | sed 's|.*/||')
```

Step 3:

```
n=$(gh issue create --title 'The upper-case rule of the greeting lives in `Greeter`' --body-file <checkout>/docs/demo/3-refactor.md --label refactor | sed 's|.*/||')
```

Step 4:

```
n=$(gh issue create --title 'Each C# file starts with an SPDX license header, and `.editorconfig` requires it' --body-file <checkout>/docs/demo/4-reframe.md --label reframe | sed 's|.*/||')
```

Each title is in single quotes, because it holds backticks. The shell runs the text between backticks in double quotes. `gh` prints the URL of the new issue, and `sed` keeps its number in `n`.

### Run and merge each issue

After each step, add the label `factory`. It goes on last, because the run reads the other labels when it starts.

```
gh issue edit "$n" --add-label factory
```

Watch the run. `gh run watch` lists the runs in progress: pick `factory`. The run ends when its pull request has green checks, and a review when `--copilot-review` is on. If `gh run watch` prints `found no in progress runs to watch`, the run is not queued yet: run it again after a few seconds.

```
gh run watch
```

The pull request holds the line `Closes #<n>`. Merge it:

```
gh pr merge "$(gh issue view "$n" --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[0].number')" --squash --delete-branch
```

After step 1, the console program shouts:

```
git pull && dotnet run --project src/App -- Ada --shout
```

It prints `HELLO, ADA!`.

## When a run fails

A run that stops at a gate or at an answer of the agent says why in a comment on the issue. A failure of `dotnet format`, and an answer of the agent that is not JSON, fail the job with no comment: read the job log.

- A failed gate is a Redo: the build runs again, with the failure text in its prompt. A test that fails, a blocking finding of the factory's own review, a check of the body that fails, a change outside the scope, and a change to the public API of a `reframe` issue each start a Redo. After 3 Redos, the run comments "The budget of 3 Redos is spent. The last build still fails:" with the failure text of the last build, and stops.
- An agent call that ends with an error stops the run. The comment holds the error text of the agent.
- A body that the factory cannot run gets a comment, and no run. The comment names each fault of the scenarios, or the first fault of a `cleanup` or `reframe` body. Two labels that exclude each other get a comment, and no run.
- Each watch of a pull request waits up to 60 minutes for the checks, and for the review when `--copilot-review` is on. After that time, the checks count as red. Add `--timeout <minutes>` to the run command in `factory.yml` to change the wait.
- The job fails when the checks are red, or when review threads stay unresolved after the review rounds. The last line of the run is `checks: <green|red>, unresolved threads: <n>`. The pull request stays open.
- The job log holds a line for the start and the end of each agent call, with its turns, spend and time. The job summary holds one row for each phase, with its time, turns, spend and result, and one item for each Redo text.

To run an issue again, remove the label `factory` and add it again.

## What the agent can touch

Claude Code runs with `--dangerously-skip-permissions`, in a docker container on the GitHub runner. The container holds the work tree of the target repo at `/work`, and it has network access.

- The agent gets `ANTHROPIC_API_KEY`, and no other secret.
- `FACTORY_GITHUB_TOKEN` never enters the container. Only the workflows, `git` and the GitHub client of the factory use it, on the runner. `git` reads it from a credential helper in its environment, so the token is never written to the work tree.
- The agent has no credential to push. The lint gate commits the change on the runner, and the factory pushes it after the tests and the review pass.
- The container mounts `.git/` of the work tree read-only, except `.git/worktrees/`, and `Git` refuses a work tree with a nested `.git`. No git hook or git config line of the agent reaches the git commands of the factory.
- Each name of `--pass-env`, such as `EXAMPLE_API_KEY`, reaches the test gate and not the agent. The tests run the code that the agent wrote, so that code can read those secrets.
- `factory.yml` and `review-feedback.yml` set `permissions` to `checks: read`, and `ci.yml` sets `contents: read`. The job's own token can do nothing more.

## What the factory does not do

- It builds only a repo that started from the starter. A clone that lacks a path of the starter's layout gets a comment, and no run.
- It runs one agent, Claude Code, with the model `claude-sonnet-5-5`.
- It merges nothing. The repo's owner merges each pull request.
- It starts no run for a label that another person adds. `factory.yml` checks that the sender is the repo's owner.

## A real program

[`sbiondolillo/forecast-sample`](https://github.com/sbiondolillo/forecast-sample) is a console program that this factory builds from its issues, one pull request for each issue. It started from the starter, and its pull request `#2` removed the greeter.

## Who builds this repo

The owner builds each change to this repo with Claude Code and the skill `story` in `.claude/skills/story/`. An issue holds a story. The owner approves it with the label `ready`, and a pull request delivers it. This repo has no `factory.yml`, so a label on one of its issues starts no run. The labels `factory`, `behavior`, `cleanup`, `refactor` and `reframe` stay on its closed issues, as the record of the runs that built it before 2026-10-09.

## Commands

```
dotnet restore --locked-mode             # the packages, from the locks
dotnet build --no-restore                # build; every warning is an error
dotnet format --verify-no-changes        # lint
dotnet test --no-restore                 # the test suite, no network needed
dotnet run --project tests/Integration   # the integration suite, network needed
dotnet run --project src/App -- Ada      # the console program
```
