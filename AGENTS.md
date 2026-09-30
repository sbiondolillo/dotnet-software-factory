# Layout

Where things are in this repo, for an agent about to read or write code here.

## Directories

`docs/agents/setup-sheet.toml` names the spec directory and the binding directory. It is the authority for those two. This table says what each directory holds.

| path | holds | rule |
|---|---|---|
| `specs/features/` | the pinned `.feature` files | a build reads them and never edits them. `tests/Specs/Specs.csproj` links every one of them in |
| `tests/Specs/` | the build suite, hermetic: Reqnroll step classes under `Steps/`, and xUnit unit tests under `Unit/` | a unit test class name ends `Tests`. A class name that ends `Feature` belongs to Reqnroll's generated code |
| `tests/Integration/` | the integration suite: xUnit tests against the real GitHub, `docker` and Claude Code | `IsTestProject` is false, so `dotnet test` never collects it. `dotnet run --project tests/Integration` runs it. A test fails when its service is unreachable |
| `src/Core/` | the library, where the behaviour lives | the test project references it |
| `src/App/` | the console program, a thin shell over the library | |

## How a scenario reaches the code

Reqnroll compiles each linked feature into a test class under `obj/`, named after the feature title with `Feature` added, and one test method per scenario. A step class marked `[Binding]` answers the steps. It calls the library in process.

Code that reads a feature file or names a test method goes in `FeatureFiles` in `src/Core/`. It reads and names with built-in functionality from the `Reqnroll` package. The package takes the version of `Reqnroll.xunit.v3`, so each name matches the test that Reqnroll compiles. A unit test of this code reads real files.

## The rules that build and style enforce

`Directory.Build.props` turns on nullable reference types, the recommended analyzers and `EnforceCodeStyleInBuild`, and makes every warning an error. `.editorconfig` holds the style. `dotnet format` fixes what it can.

The house style as prose, for the rules no analyzer expresses: a class that stores its constructor arguments uses a primary constructor, and a comparison with a constant is a pattern: `value is null`, `value is not null`, `result.ExitCode is not 0`, `value is string text`. `is not 0` over `!= 0` is the one rule no analyzer flags; the build will not catch it, so write it by hand. Every `await` in `src/` ends with `.ConfigureAwait(false)`. A public async method takes `CancellationToken cancellationToken = default` as its last parameter, and passes it to every call that takes one.

## Git

`AGENTS.md` states the rules every git command follows, so a change that
adds a git operation to `src/Core/Git.cs` follows them too. The build suite mocks `git`
through `CommandRunner`, so only the integration job shows the failure each rule below
prevents.

- `Git` is the one class that runs `git`. Every command is a `CommandSpec` whose program
  is `git`, run through the `CommandRunner` that `Git` is built over. Every command after
  the clone takes the clone's directory as its working directory.
- The token lives only in the environment variable `FACTORY_GITHUB_TOKEN`. The remote URL
  is `https://github.com/<owner>/<repo>.git`, and no argument holds the token.
- A command that talks to GitHub carries a credential helper in two `GIT_CONFIG_*`
  entries: `GIT_CONFIG_VALUE_0` is the empty string, which clears the machine's own
  helpers, and `GIT_CONFIG_VALUE_1` is a shell helper that echoes
  `username=x-access-token` and `password=$FACTORY_GITHUB_TOKEN`. Git asks for a username
  first, and off a terminal a helper that answers only `password=` fails every push with
  `could not read Username`.
- A new branch is `checkout -b <branch>`. `checkout <branch>` creates a branch only when a
  remote branch of that name exists, and fails in a fresh clone with
  `did not match any file(s) known to git`.
- A commit carries `GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME` and
  `GIT_COMMITTER_EMAIL` in its environment. The clone and a CI runner have no git
  identity, and a commit with no committer identity fails.
- Whether a work tree holds a change is `status --porcelain`: each line of its standard
  output is one file that is changed, removed or new. `commit` with no change exits with
  the code 1 and an empty standard error, so a caller that commits a work tree asks
  first.
- The files changed since a base are `diff --name-only <base>`, with the base as one
  argument. The range form `<base>..HEAD` names the range in its error, so the error for
  an unknown base does not hold the base's name as given.
- A git command that exits with a code other than 0 throws `GitCommandException`, whose
  message names the subcommand and holds the standard error.
- An integration test reads the git author of a branch from GitHub's branch API at
  `commit.commit.author`. The API's `commit.author` is the GitHub account, and it is
  `null` for an email that no account owns.
- Every request an integration test sends to GitHub's API carries a `User-Agent` header.
  GitHub answers a request with none with `403 (Forbidden)`.

## Docker

`AGENTS.md` states the rules every `docker` command follows, so a class that runs
`docker` follows them too. The build suite mocks `docker` through `CommandRunner`, so
only the integration job shows the failure each rule below prevents.

- Every command is a `CommandSpec` whose program is `docker`, run through the
  `CommandRunner` that the class is built over.
- A container that runs commands on a work tree runs as the user id and the group id of
  the process that starts it: `--user <uid>:<gid>`. The user of the image has one fixed
  id. On a work tree that a different user id owns, each write of that user fails with
  `Permission denied`.
- That container has a home directory that its user can write, with the directory
  `.config` in it, and `HOME` names that directory. For a user id that the image does not
  hold, `HOME` reads `/`, and `dotnet` fails in its first-run setup. With no `.config` in
  the home directory, `dotnet test` writes `Reqnroll/userid` into the test project.
- That container starts with `--init`, and its first command runs until the removal.
  `dotnet test` leaves defunct processes behind it, and the init process removes them. A
  container with no first command exits at once, and each `docker exec` then exits with
  the code 1.
- An environment variable goes to `docker` by its name. The argument after `--env` is the
  name alone, and `docker` reads the value from its own environment. So no argument holds
  the value of a secret. A name on `docker run` gives the variable to every command in
  the container, and a name on `docker exec` gives it to that one command.
- The removal of a container is `docker rm --force`, and it ends every process in the
  container. A `docker exec` that stops before its command ends leaves the command alive
  in the container.

## Claude Code

`AGENTS.md` states the rules every `claude` command follows, so a class that runs
`claude` follows them too. The build suite mocks `claude` through `CommandRunner`, so
only the integration job shows the failure each rule below prevents.

- Every command runs through a `Sandbox`, with an empty list of names. The container
  holds `ANTHROPIC_API_KEY` from its start, and `claude` reads the key from its
  environment.
- `claude` runs with `-p`, `--output-format json` and `--dangerously-skip-permissions`.
  Without the bypass, `claude` denies itself each tool call, lists it under
  `permission_denials`, asks for permission in `result` and exits with the code 0.
  `claude` refuses the bypass as root with the exit code 1 and no JSON object, so the
  container runs as the caller's user, which `## Docker` states.
- The prompt is one argument of 131071 bytes or fewer. An argument of 131072 bytes or
  more fails the start of `docker` on the host with `Argument list too long`, before
  `docker` runs. Standard input is no route: `docker exec` attaches none without
  `--interactive`, and `CommandRunner` redirects none.
- The answer is one JSON object and a line feed on standard output. `is_error` true
  with the exit code 1 is an answer: `result` is a sentence on an API error, and it is
  absent on `error_max_turns`. An exit code other than 0 with no JSON object on
  standard output is a failure of the start, and standard error holds the reason.
- `num_turns` reads one over `--max-turns` when the limit ends the run, and a file the
  agent was writing lands all the same.

## GitHub

`GitHubClient` sends every request to GitHub, so a class that talks to GitHub sends
through it. A request that `GitHubClient` sends again goes through the
`Microsoft.Extensions.Http.Resilience` package, which holds the retry, the wait
between requests and the reading of `Retry-After`.

## dotnet

`AGENTS.md` states the rules every `dotnet` command in a sandbox follows, so a class
that runs `dotnet` through a `Sandbox` follows them too. The build suite mocks `docker`
through `CommandRunner`, so only the integration job shows the failure each rule below
prevents.

- The exit code alone says whether a command passed. The first `dotnet build` or
  `dotnet format` of a container exits with the code 0 and writes a line to standard
  error that starts `An issue was encountered verifying workloads`.
- `dotnet format` runs with `--no-restore`. Without it, `dotnet format` restores first,
  outside locked mode. That restore rewrites a `packages.lock.json` that the project
  files no longer match, and off the network it fails with `Restore operation failed`
  and the exit code 1.
- `dotnet format` formats code that does not compile and exits with the code 0, so only
  the build shows a compile error.
- `dotnet restore` runs with `--locked-mode`. A project whose package references differ
  from its `packages.lock.json` then fails with `error NU1004` on standard output and the
  exit code 1, and each lock file stays as it was.
- `dotnet build`, `dotnet test` and `dotnet run` run with `--no-restore` after that
  restore. Without it, each one restores first, outside locked mode, and on the network
  that restore rewrites every `packages.lock.json` and exits with the code 0.
- `dotnet format --verify-no-changes` exits with the code 2 when a file needs a format,
  and writes one line for each such file to standard error, in the form
  `<path>(<line>,<column>): error WHITESPACE: Fix whitespace formatting.` With no such
  file it exits with the code 0 and writes nothing.
- `dotnet test --logger trx` writes one TRX file for each test project under
  `TestResults/` in that project's directory, named from the host and the time, and
  every earlier file there stays. `--logger "trx;LogFileName=<name>"` names the file,
  and a later run rewrites it. `--results-directory <dir>` puts it under `<dir>`.
- `dotnet test` exits with the code 1 when a test fails, and the TRX file holds that
  test with the outcome `Failed` and its message. A scenario with an unbound step is
  such a test, and its output holds `No matching step definition found`. A skipped test
  has the outcome `NotExecuted`, and the exit code stays 0. When the test project does
  not compile, `dotnet test` exits with the code 1 and writes no TRX file, so the file
  of an earlier run stays where it was.
- `dotnet run --project tests/Integration` exits with the code 0 when every test passes
  and when the project holds no test, and with the code 1 when a test fails. The summary
  goes to standard output, with terminal colour codes, and its last line of substance is
  `Integration  Total: <n>, Errors: <n>, Failed: <n>, Skipped: <n>, Not Run: <n>, Time: <s>`.
- The restore puts the packages in the NuGet cache under the home directory of the
  container, so a command that needs them runs in the container that ran the restore.
