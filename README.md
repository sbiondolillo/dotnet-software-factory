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

Before the first command, you have:

- a GitHub account, on any plan
- `gh` signed in as that account: `gh auth status` names it
- the .NET SDK that the starter's `global.json` pins, 10.0
- an Anthropic API key
- a clone of this repo at the tag `v1`: `git clone --branch v1 https://github.com/sbiondolillo/dotnet-software-factory.git`

Set the two values the commands below read: the path of that clone, and the name of the new repo:

```
factory=/path/to/dotnet-software-factory
name=my-factory-repo
```

The variables live in this terminal. A new terminal runs this block again. Every command below runs in this terminal, in order.

Make a new repo from the starter template:

```
dotnet new install "$factory/templates/starter"
dotnet new factory-starter -o "$name"
```

Go into it. Every command below runs there:

```
cd "$name"
```

Push it to the branch `main` of a new GitHub repo. `--public` makes a public repo, and `--private` a private one. See `## A private target repo` for what differs on a private repo:

```
git init -b main && git add -A && git commit -m "Start from dotnet-software-factory v1"
gh repo create "$name" --public --source . --remote origin --push
```

Make `FACTORY_GITHUB_TOKEN` in your browser, at https://github.com/settings/personal-access-tokens/new. Make it after the new repo exists, because a fine-grained token can select only a repo that exists. Under Repository access, select the new repo alone, and give the token read and write access to Contents, Issues, Pull requests and Workflows. Workflows lets a run push a change under `.github/workflows/`.

Set it as the Actions secret `FACTORY_GITHUB_TOKEN` of the new repo. The command asks for the value and reads it hidden, so the value stays out of the shell history:

```
gh secret set FACTORY_GITHUB_TOKEN
```

Make an Anthropic API key in your browser, at https://console.anthropic.com/settings/keys. The factory spends it on each run.

Set it as the Actions secret `ANTHROPIC_API_KEY`, the same way:

```
gh secret set ANTHROPIC_API_KEY
```

Protect the branch `main`, with the checks `build` and `integration` required. Run this command in the checkout of the new repo, and `gh` fills in `{owner}` and `{repo}`:

```
gh api -X PUT repos/{owner}/{repo}/branches/main/protection --input - <<'JSON'
{"required_status_checks":{"strict":false,"contexts":["build","integration"]},"enforce_admins":false,"required_pull_request_reviews":null,"restrictions":null}
JSON
```

GitHub applies the protection on a public repo, and on a private repo of a paid plan. The command sets `enforce_admins` to `false`, so an admin of the repo, such as its owner, can still merge a pull request with red checks. See `## A private target repo`.

Create the five labels that the factory reads. `--force` updates a label that exists:

```
for label in factory cleanup refactor reframe review-feedback; do gh label create "$label" --force; done
```

The repo is ready. `## Run the demo` is its first run.

## A private target repo

The factory runs on a private repo, with these limits:

- GitHub enforces branch protection on a private repo only on a paid plan. On the free plan of a personal account, GitHub shows "Your rules won't be enforced on this private repository until you move to a GitHub Team or Enterprise organization account". The factory still waits for green checks, but GitHub lets each person with write access merge a pull request with red checks.
- `FACTORY_GITHUB_TOKEN` must select each target repo, public or private. A new repo needs a new token.
- An organization can require approval of each fine-grained token. Until an administrator approves it, the token reads only public resources.
- GitHub never shows a secret again after you save it. Keep each value in a store outside GitHub, such as a password manager.
- The job's own token reads the check runs, as `FACTORY_CHECKS_TOKEN` in `## Tokens and secrets`. The repo's owner sets nothing for it.
- A Copilot code review on a private repo is not tested. The trial on a private repo ran with `--copilot-review off`.

## Run the demo

Four issues on the greeter show the four kinds of run. Each body is a file under `docs/demo/` of the factory, and `$factory` from `## Start a new repo` names it. Each step files an issue and labels it `factory`, watches the run, then merges the pull request and pulls `main`. Run the steps in order. The second and the third issue need the option `--shout`, which the first adds.

| Step | Kind | Body | What the pull request does |
|---|---|---|---|
| 1 | `behavior` | `docs/demo/1-behavior.md` | adds the option `--shout`, and three scenarios |
| 2 | `cleanup` | `docs/demo/2-cleanup.md` | adds `README.md`, byte for byte with the text in the body |
| 3 | `refactor` | `docs/demo/3-refactor.md` | moves the upper-case rule into `Greeter`, and changes no test |
| 4 | `reframe` | `docs/demo/4-reframe.md` | requires an SPDX license header on each C# file, and adds it |

Each title is in single quotes, because it holds backticks. `gh` prints the URL of the new issue, and `sed` keeps its number in `n`. The label `factory` goes on last, because the run reads the other labels when it starts.

`gh run watch` lists the runs in progress: pick `factory`. The run ends when its pull request has green checks. If `gh run watch` prints `found no in progress runs to watch`, the run is not queued yet: run it again after a few seconds.

The pull request holds the line `Closes #<n>`, and the merge command finds it from the issue.

### Step 1

```
n=$(gh issue create --title 'A person can print the greeting in upper case with the option `--shout`' --body-file "$factory/docs/demo/1-behavior.md" | sed 's|.*/||') && gh issue edit "$n" --add-label factory && echo "issue $n"
```

```
gh run watch
```

```
gh pr merge "$(gh issue view "$n" --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[0].number')" --squash --delete-branch && git pull && dotnet run --project src/App -- Ada --shout
```

The last command prints `HELLO, ADA!`.

### Step 2

```
n=$(gh issue create --title 'The repo has a `README.md` that says what the program does and how to run and test it' --body-file "$factory/docs/demo/2-cleanup.md" --label cleanup | sed 's|.*/||') && gh issue edit "$n" --add-label factory && echo "issue $n"
```

```
gh run watch
```

```
gh pr merge "$(gh issue view "$n" --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[0].number')" --squash --delete-branch && git pull
```

### Step 3

```
n=$(gh issue create --title 'The upper-case rule of the greeting lives in `Greeter`' --body-file "$factory/docs/demo/3-refactor.md" --label refactor | sed 's|.*/||') && gh issue edit "$n" --add-label factory && echo "issue $n"
```

```
gh run watch
```

```
gh pr merge "$(gh issue view "$n" --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[0].number')" --squash --delete-branch && git pull
```

### Step 4

```
n=$(gh issue create --title 'Each C# file starts with an SPDX license header, and `.editorconfig` requires it' --body-file "$factory/docs/demo/4-reframe.md" --label reframe | sed 's|.*/||') && gh issue edit "$n" --add-label factory && echo "issue $n"
```

```
gh run watch
```

```
gh pr merge "$(gh issue view "$n" --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[0].number')" --squash --delete-branch && git pull
```

After step 4, every C# file starts with `// SPDX-License-Identifier: MIT`. You have run the four kinds. `## The kinds of run` says what each one asks of an issue.

## Turn on Copilot code review

The factory can request a Copilot code review of each pull request it watches, and wait for that review on the head commit as well as the checks. A review round that leaves threads unresolved fails the job, and `review-feedback` runs answer the threads. This needs a plan that includes Copilot code review, with credit on it. A repo made from the starter runs with no review: its workflows pass no `--copilot-review`, and the factory then waits for the checks alone.

With a Copilot plan, add `--copilot-review on` to the run command in `factory.yml` and in `review-feedback.yml`. Two ways do it. The first is a direct edit and push, as the repo's owner:

```
sed -i 's/--pass-env EXAMPLE_API_KEY$/--pass-env EXAMPLE_API_KEY --copilot-review on/' .github/workflows/factory.yml .github/workflows/review-feedback.yml && git add .github/workflows && git commit -m "Run the factory with --copilot-review on" && git push
```

The second is a fifth demo step, a `cleanup` issue that the factory builds. Its body names the two workflow files as its scope, and its check greps both for the flag:

### Step 5

```
n=$(gh issue create --title 'The factory requests a Copilot code review of each pull request' --body-file "$factory/docs/demo/5-copilot-on.md" --label cleanup | sed 's|.*/||') && gh issue edit "$n" --add-label factory && echo "issue $n"
```

```
gh run watch
```

```
gh pr merge "$(gh issue view "$n" --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[0].number')" --squash --delete-branch && git pull
```

❗ With `on` and no Copilot on the account, the request to the reviewer returns no error, and the run watches for the review to the 60-minute timeout. The checks then count as red, and the job fails with the pull request open. Turn the review on only on an account that has it.

## The kinds of run

The repo's owner starts a run with the label `factory` on an issue. `factory.yml` names the tag `v1` of the factory.

The body of an issue can hold its scenarios under the heading `## Scenarios`, and the factory then pins them as the owner wrote them. Each fenced block under the heading starts with the line `# specs/features/<name>.feature`. A whole-file block holds a `Feature:` line, and it becomes the file or replaces the file on `main`. Any other block is an upsert into the file on `main`. A scenario whose name the file holds replaces that scenario in place, and a scenario with a new name goes at the end of the file. Under the heading `## Removed scenarios`, each list item names one scenario to remove, as `` `specs/features/<name>.feature`: <scenario name> ``. The plan phase then writes only `.factory/plan.md`. A body whose scenarios are not sound gets a comment that names each fault, and no run.

The repo's owner starts a feedback run with the label `review-feedback` on a pull request. The run removes the label, so the owner applies it again for the next round.

The repo's owner starts a cleanup run, a run with no plan, with the label `cleanup` on an issue that fixes a test, a config file or a document. The label `cleanup` and the label `behavior` on one issue get a comment and no run.

The body of a `cleanup` issue names the paths the build can change under the heading `## Scope`, one for each line. A line is a path, either bare or as a Markdown list item with the marker `-`, `*` or `+`, and the path can sit in backticks. A directory ends in `/`, and a path with `*` is a glob. The body holds under the heading `## Check` one fenced `sh` block, the check command, that fails before the change and exits with the code 0 after it. The factory runs the command with `sh -c` in a copy of the work tree, and a command that runs `dotnet` restores its packages itself first.

A feedback run on the pull request of a `cleanup` issue holds the build to the issue's `## Scope` and runs its `## Check` after the tests pass.

The repo's owner starts a refactor run with the label `refactor` on an issue that changes the structure of the code and adds no behaviour. The plan of a refactor run writes no feature file, and the build leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is. The body can hold under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it, and the factory runs it as it runs the check of a `cleanup` issue. The label `refactor` with the label `behavior` or the label `cleanup` on one issue gets a comment and no run.

A feedback run on the pull request of a `refactor` issue leaves every file under `specs/features/`, `tests/Specs/` and `tests/Integration/` as it is, and runs the issue's `## Check` after the tests pass.

The repo's owner starts a reframe run, a run with no plan, with the label `reframe` on an issue. A reframe issue keeps the behaviour of the program and changes the code, the tests or the config. A format sweep and a framework upgrade are reframe issues. The body names the paths the build can change under the heading `## Scope`, as a `cleanup` issue does. The scope can name `src/`. The body holds under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it. The run records the tests that pass on the base commit. It also records the public API of the code under `src/`: one line per public type and per public or protected member. The build runs again when it loses a test or changes a line of that API. The label `reframe` with the label `behavior`, `cleanup` or `refactor` on one issue gets a comment and no run.

## A trial run at another ref

The repo's owner starts a trial run of the factory at another ref with the command `gh workflow run factory -f issue=<number> -f ref=<ref>`. The ref is a branch, a tag or a commit.

## Tokens and secrets

`factory.yml` and `review-feedback.yml` check the factory out with no token, because the factory is public.

`factory.yml` and `review-feedback.yml` pass the job's own token to the factory as `FACTORY_CHECKS_TOKEN`, with the permission `checks: read`. The factory reads the check runs of each pull request with it, on a public repo and on a private repo alike. A fine-grained token has no Checks permission, so `FACTORY_GITHUB_TOKEN` reads check runs on a public repo only.

Replace `EXAMPLE_API_KEY` in `factory.yml`, in `review-feedback.yml` and in `ci.yml` with the name of each secret the integration tests of the new repo read.

A repo whose integration tests read no secret keeps `EXAMPLE_API_KEY` as it is. The secret is not set, and each step gets an empty value.

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

