Feature: A reframe run keeps every test and every public type and member

  Background:
    Given the repo "factory-scratch" of the owner "sbiondolillo", the issue 12 and the token "token"
    And the run id "a1b2c3d4", the base branch "main" and an empty list of secret names
    And the plan model "plan-model" with turn limit 20, the build model "build-model" with turn limit 60 and the review model "review-model" with turn limit 30
    And the author name "Factory" and the author email "factory@example.com"
    And the clone holds the four paths of the layout and its head commit is "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c"
    And git reports a change in the work tree after each build
    And claude answers each prompt with the error flag clear
    And each dotnet command exits with the code 0 and the TRX file holds one passed test
    And the agent approves on the review prompt
    And GitHub answers the open of the pull request with the number 10 and the web address "https://github.com/sbiondolillo/factory-scratch/pull/10"

  Scenario: A reframe run runs no plan prompt
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then FactoryRun runs no prompt with the plan model

  Scenario: The labels reframe and behavior get a comment
    Given the issue has the labels "reframe" and "behavior"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "reframe" and "behavior"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: The labels reframe and cleanup get a comment
    Given the issue has the labels "reframe" and "cleanup"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "reframe" and "cleanup"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: The labels reframe and refactor get a comment
    Given the issue has the labels "reframe" and "refactor"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "reframe" and "refactor"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A body with no Scope heading gets a comment
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with no heading "## Scope"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "## Scope"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A body with no Check heading gets a comment
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with no heading "## Check"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "## Check"
    And FactoryRun starts no container
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: The check command runs under sh before the build prompt
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then the container runs "sh -c" with the argument "grep -q Install README.md" before the build prompt

  Scenario: The check command runs in a detached worktree outside /work
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then the container runs "git worktree add --detach" at a path outside "/work" and the check command runs in that path on the head commit

  Scenario: The copy is removed when the check command ends
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    When a caller starts a run
    Then the container runs "git worktree remove --force" on the copy after the check command ends

  Scenario: A check that passes before the build gets a comment
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 0 before the build
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "The check passes before the build"
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A check that exits with 126 gets a comment with its standard error
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 126 before the build and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "126" and the standard error of the command
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A check that exits with 127 gets a comment with its standard error
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 127 before the build and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "127" and the standard error of the command
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A red check runs the API printer before the build prompt
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then the container runs "dotnet" with the arguments "/opt/api-printer/ApiPrinter.dll" and "src" before the build prompt

  Scenario: The API printer runs in a built detached worktree outside /work
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then the container runs "git worktree add --detach" at a path outside "/work", and in that path "dotnet restore --locked-mode" and then "dotnet build --no-restore" run before the API printer

  Scenario: The copy is removed when the API printer ends
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then the container runs "git worktree remove --force" on the copy after the API printer ends

  Scenario: A copy that does not build gets a comment with the build output
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And dotnet build in the copy exits with the code 1 before the build and writes a standard output
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "dotnet build" and the standard output of the copy's build
    And FactoryRun runs no API printer
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: An API printer that finds no assembly before the build gets a comment
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the API printer exits with the code 3 before the build and writes the standard error "no built assembly: no *.deps.json with a .dll beside it under /work/src"
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds "the code 3" and the standard error of the API printer
    And FactoryRun runs no build prompt
    And FactoryRun gives back a result with no pull request and 0 Redos

  Scenario: A listed API starts the build prompt with the issue
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "build-model" and the turn limit 60 that holds the title and the body of the issue
    And the build prompt holds this text
      """
      Make the change the issue asks for to its config files first, then run dotnet format --no-restore once from the repo root, before any edit to a source file. Edit by hand only what the build still reports.
      """

  Scenario: The reframe build prompt names no plan
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    When a caller starts a run
    Then the build prompt holds no ".factory/plan.md"

  Scenario: A change outside the scope gets a Redo
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "docs/README.md"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "outside the scope: docs/README.md"
    And no review prompt runs before it

  Scenario: A change under src with the same tests and API reaches the review
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "src/Core/Greeter.cs"
    And each test that passed on the base commit passes
    And the API printer prints the same lines as on the base commit
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt
    And the review prompt holds "keeps every test and every public type and member"

  Scenario: A change to a feature file gets a pin Redo
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "specs/features/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only "specs/features/greeting.feature"
    When a caller starts a run
    Then the next build prompt holds "Make the change the issue asks for to its config files first"
    And the next build prompt holds a Redo text that holds "pin: specs/features/greeting.feature"
    And no review prompt runs before it

  Scenario: An entry for a settings file covers it
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope ".editorconfig" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the build changes only ".editorconfig"
    And each test that passed on the base commit passes
    And the API printer prints the same lines as on the base commit
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt
    And the review prompt holds "keeps every test and every public type and member"

  Scenario: A lost test gets a Redo
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the test "Specs.Unit.GreeterTests.Greets" passed on the base commit
    And the TRX file after the build holds no test of the class "Specs.Unit.GreeterTests" and the method "Greets"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "lost test: Specs.Unit.GreeterTests.Greets"
    And no review prompt runs before it

  Scenario: A new passed test with no lost test reaches the review
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the TRX file after the build holds a passed test that the base commit lacks
    And each test that passed on the base commit passes
    And the API printer prints the same lines as on the base commit
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs the review prompt
    And the review prompt holds "keeps every test and every public type and member"

  Scenario: A lost symbol gets a Redo
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the API printer prints the line "Core: Core.Greeter::method public static System.String Greet(System.String)" on the base commit
    And each test that passed on the base commit passes
    And after the build the API printer prints every other line of the base commit and no such line
    When a caller starts a run
    Then the next build prompt holds a Redo text with "lost symbol:" once, in "lost symbol: Core: Core.Greeter::method public static System.String Greet(System.String)", and no "new symbol:"
    And no review prompt runs before it

  Scenario: A new symbol gets a Redo
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the API printer prints no line "Core: class static Core.Extra" on the base commit
    And each test that passed on the base commit passes
    And after the build the API printer prints each line of the base commit and the line "Core: class static Core.Extra"
    When a caller starts a run
    Then the next build prompt holds a Redo text with "new symbol:" once, in "new symbol: Core: class static Core.Extra", and no "lost symbol:"
    And no review prompt runs before it

  Scenario: An API printer that finds no assembly after the build gets a Redo
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And each test that passed on the base commit passes
    And the API printer exits with the code 3 after the build and writes the standard error "no built assembly: no *.deps.json with a .dll beside it under /work/src"
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "the code 3" and the standard error of the API printer
    And no review prompt runs before it

  Scenario: A red check after a build gets a Redo with its output
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And each test that passed on the base commit passes
    And the API printer prints the same lines as on the base commit
    And the check command exits with the code 1 after the build and writes a standard output and a standard error
    When a caller starts a run
    Then the next build prompt holds a Redo text that holds "1", the standard output and the standard error of the command
    And no review prompt runs before it

  Scenario: A green check after a build starts the review prompt
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And each test that passed on the base commit passes
    And the API printer prints the same lines as on the base commit
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun runs one prompt with the model "review-model" and the turn limit 30 that holds the title of the issue, the body of the issue and the base commit
    And the review prompt holds this text
      """
      Judge the build against every sentence of the issue: it changes only the paths under ## Scope, keeps the behaviour of the program and makes the command under ## Check pass, and it keeps every test and every public type and member of the code under src/.
      """

  Scenario: The reframe review prompt names no plan
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command then exits with the code 0
    When a caller starts a run
    Then the review prompt holds no ".factory/plan.md"

  Scenario: An approved reframe build opens a pull request
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And the check command then exits with the code 0
    When a caller starts a run
    Then FactoryRun opens a pull request with the title "Sweep the format of src" and gives back a result with that pull request and 0 Redos
    And the body of the pull request holds the line "- check: green"

  Scenario: Four red builds give a comment and no pull request
    Given the issue has the label "reframe", the title "Sweep the format of src" and a body with the scope "src/" and the first fenced block under "## Check" holding the line "grep -q Install README.md"
    And the check command exits with the code 1 before the build
    And each test that passed on the base commit passes
    And the API printer prints the same lines as on the base commit
    And the check command exits with the code 1 after each of four builds and writes a standard error
    When a caller starts a run
    Then FactoryRun posts one comment on the issue that holds the standard error of the command
    And FactoryRun pushes no branch
    And FactoryRun gives back a result with no pull request and 3 Redos

  Scenario: The program writes one progress line for each reframe step
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the issue 12 has the label "reframe" and each step passes
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds exactly these lines in this order
      | line |
      | clone: start |
      | sandbox: start |
      | check: start |
      | check: red |
      | api: start |
      | api: listed |
      | build: start |
      | lint: start |
      | test: start |
      | test: passed |
      | api: start |
      | api: same |
      | check: start |
      | check: green |
      | review: start |
      | review: approved |
      | deliver: start |
      | https://github.com/sbiondolillo/factory-scratch/pull/10 |
      | watch: start |
      | checks: green, unresolved threads: 0 |

  Scenario: A redo line sits between the changed API and the next build
    Given the environment holds "FACTORY_GITHUB_TOKEN" and "ANTHROPIC_API_KEY"
    And the issue 12 has the label "reframe"
    And the API printer of the program prints no line "Core: class static Core.Extra" on the base commit, and after the first build it prints that line and each line of the base commit
    When the program starts with the arguments "run --repo sbiondolillo/factory-scratch --issue 12"
    Then standard output holds the lines in this order with no line between them
      | line |
      | api: start |
      | api: changed |
      | redo: 1 |
      | build: start |

  Scenario: The README documents the reframe run
    When a reader reads the section "## Start a new repo" of README.md
    Then the section mentions the label "reframe"
    And the section holds, after the paragraph that starts "A feedback run on the pull request of a `refactor` issue", this paragraph
      """
      Create the label `reframe` in the new repo. The repo's owner starts a reframe run, a run with no plan, with the label `reframe` on an issue. A reframe issue keeps the behaviour of the program and changes the code, the tests or the config. A format sweep and a framework upgrade are reframe issues. The body names the paths the build can change under the heading `## Scope`, as a `cleanup` issue does. The scope can name `src/`. The body holds under the heading `## Check` one fenced `sh` block that fails before the change and exits with the code 0 after it. The run records the tests that pass on the base commit. It also records the public API of the code under `src/`: one line per public type and per public or protected member. The build runs again when it loses a test or changes a line of that API. The label `reframe` with the label `behavior`, `cleanup` or `refactor` on one issue gets a comment and no run.
      """

  Scenario: The sandbox Dockerfile publishes the API printer
    Then the file "sandbox/Dockerfile" of this repo holds this text
      """
      FROM node:22-bookworm-slim

      ARG DOTNET_SDK_VERSION

      RUN apt-get update \
          && apt-get install --no-install-recommends -y git curl ca-certificates libicu72 \
          && rm -rf /var/lib/apt/lists/*

      RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
          && bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir /usr/share/dotnet \
          && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
          && rm /tmp/dotnet-install.sh

      RUN npm install -g @anthropic-ai/claude-code

      ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

      # The public API printer of a reframe run, published on the image's own runtime.
      # A run lists the public API of the code under src/ with
      # `dotnet /opt/api-printer/ApiPrinter.dll src`.
      COPY sandbox/ApiPrinter /tmp/api-printer
      RUN cd /tmp/api-printer \
          && dotnet publish -c Release -o /opt/api-printer \
          && chmod -R a+rX /opt/api-printer \
          && rm -rf /tmp/api-printer /root/.nuget /root/.local/share/NuGet

      USER node
      RUN mkdir -p /home/node/.config
      WORKDIR /home/node
      """

  Scenario: The API printer project holds the pinned text
    Then the file "sandbox/ApiPrinter/ApiPrinter.csproj" of this repo holds this text
      """
      <Project Sdk="Microsoft.NET.Sdk">
        <!-- The public API printer of a reframe run. sandbox/Dockerfile publishes it
             into the image at /opt/api-printer, so a run reads a workpiece's bin/
             through the image's own runtime, whatever the workpiece targets. -->
        <PropertyGroup>
          <OutputType>Exe</OutputType>
          <TargetFramework>net10.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <RollForward>LatestMajor</RollForward>
          <InvariantGlobalization>true</InvariantGlobalization>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="System.Reflection.MetadataLoadContext" Version="10.0.0" />
        </ItemGroup>
      </Project>
      """

  Scenario: The API printer program holds the pinned text
    Then the file "sandbox/ApiPrinter/Program.cs" of this repo holds this text
      """
      // Prints the public API of every assembly a project built under the given
      // directories: one line per public type and per public or protected member,
      // sorted, so two listings diff symbol by symbol. A reframe run lists the
      // base commit and each build with it, and compares the two listings.
      //
      // WHICH ASSEMBLIES. A project's own output has a `<name>.deps.json` beside
      // `<name>.dll`; a dependency copied into another project's `bin/` has none.
      // So every `*.deps.json` found under the directories names one assembly.
      //
      // HOW THEY LOAD. `MetadataLoadContext` reads metadata without running code,
      // against the runtime this program runs on, so a workpiece's own target
      // framework needs no matching runtime here. The resolver sees every `.dll`
      // under the directories once, by file name, plus the runtime's own.
      using System.Reflection;
      using System.Runtime.InteropServices;

      public static class Api
      {
          public static int Main(string[] args)
          {
              if (args.Length == 0)
              {
                  Console.Error.WriteLine("usage: ApiPrinter <directory>...");
                  return 2;
              }

              var roots = args.Select(Path.GetFullPath).ToList();
              var targets = roots
                  .SelectMany(root => Directory.EnumerateFiles(root, "*.deps.json", SearchOption.AllDirectories))
                  .Select(deps => Path.ChangeExtension(deps, ".dll").Replace(".deps.dll", ".dll"))
                  .Where(File.Exists)
                  .Distinct()
                  .OrderBy(path => path, StringComparer.Ordinal)
                  .ToList();
              if (targets.Count == 0)
              {
                  Console.Error.WriteLine("no built assembly: no *.deps.json with a .dll beside it under " + string.Join(", ", roots));
                  return 3;
              }

              var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
              foreach (string dll in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
              {
                  paths.TryAdd(Path.GetFileName(dll), dll);
              }
              foreach (string root in roots)
              {
                  foreach (string dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
                  {
                      paths.TryAdd(Path.GetFileName(dll), dll);
                  }
              }

              var lines = new SortedSet<string>(StringComparer.Ordinal);
              using (var context = new MetadataLoadContext(new PathAssemblyResolver(paths.Values), "System.Private.CoreLib"))
              {
                  foreach (string target in targets)
                  {
                      Assembly assembly = context.LoadFromAssemblyPath(target);
                      string name = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(target);
                      foreach (Type type in assembly.GetTypes().Where(IsVisible))
                      {
                          lines.Add($"{name}: {Api.Describe(type)}");
                          foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                          {
                              if (Api.Describe(member) is string line)
                              {
                                  lines.Add($"{name}: {type.FullName}::{line}");
                              }
                          }
                      }
                  }
              }
              foreach (string line in lines)
              {
                  Console.WriteLine(line);
              }
              return 0;
          }

          public static bool IsVisible(Type type) => type.IsPublic || type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem;

          public static string Describe(Type type)
          {
              string kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
              string mods = string.Join(" ", new[]
              {
              type.IsAbstract && type.IsSealed ? "static" : null,
              type.IsAbstract && !type.IsSealed && !type.IsInterface ? "abstract" : null,
              type.IsSealed && !type.IsAbstract && !type.IsValueType && !type.IsEnum ? "sealed" : null,
          }.Where(m => m is not null));
              var bases = new List<string>();
              if (type.BaseType is Type b && b.FullName is not "System.Object" and not "System.ValueType" and not "System.Enum")
              {
                  bases.Add(b.ToString());
              }
              bases.AddRange(type.GetInterfaces().Where(IsVisible).Select(i => i.ToString()).OrderBy(s => s, StringComparer.Ordinal));
              string inherits = bases.Count == 0 ? "" : " : " + string.Join(", ", bases);
              return $"{kind} {(mods.Length == 0 ? "" : mods + " ")}{type.FullName}{inherits}";
          }

          public static string? Describe(MemberInfo member)
          {
              switch (member)
              {
                  case MethodBase method when IsVisible(method) && !method.IsSpecialName:
                      return $"{(method is ConstructorInfo ? "ctor" : "method")} {Access(method)} {(method.IsStatic ? "static " : "")}{method}";
                  case PropertyInfo property:
                      MethodInfo?[] accessors = [property.GetMethod, property.SetMethod];
                      if (!accessors.Any(a => a is not null && IsVisible(a)))
                      {
                          return null;
                      }
                      string get = property.GetMethod is MethodInfo g && IsVisible(g) ? $" {Access(g)} get;" : "";
                      string set = property.SetMethod is MethodInfo s && IsVisible(s) ? $" {Access(s)} set;" : "";
                      return $"property {property.PropertyType} {property.Name} {{{get}{set} }}";
                  case FieldInfo field when IsVisible(field):
                      string fmods = field.IsLiteral ? "const " : (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : "");
                      return $"field {Access(field)} {fmods}{field.FieldType} {field.Name}";
                  case EventInfo ev when ev.AddMethod is MethodInfo add && IsVisible(add):
                      return $"event {Access(add)} {ev.EventHandlerType} {ev.Name}";
                  default:
                      return null;
              }
          }

          public static bool IsVisible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

          public static bool IsVisible(FieldInfo f) => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;

          public static string Access(MethodBase m) => m.IsPublic ? "public" : "protected";

          public static string Access(FieldInfo f) => f.IsPublic ? "public" : "protected";
      }
      """
