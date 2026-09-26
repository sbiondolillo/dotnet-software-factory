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

## The rules that build and style enforce

`Directory.Build.props` turns on nullable reference types, the recommended analyzers and `EnforceCodeStyleInBuild`, and makes every warning an error. `.editorconfig` holds the style. `dotnet format` fixes what it can.

The house style as prose, for the rules no analyzer expresses: a class that stores its constructor arguments uses a primary constructor, and a comparison with a constant is a pattern: `value is null`, `value is not null`, `result.ExitCode is not 0`, `value is string text`. `is not 0` over `!= 0` is the one rule no analyzer flags; the build will not catch it, so write it by hand. Every `await` in `src/` ends with `.ConfigureAwait(false)`.

## Git

`docs/agents/layout.md` states the rules every git command follows, so a change that
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
