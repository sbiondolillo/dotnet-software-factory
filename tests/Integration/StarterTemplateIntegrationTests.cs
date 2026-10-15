using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the starter template's canned answers are what the real dotnet gives: each test but the install test makes a copy of the template that the class installs one time, and runs the command of its row.</summary>
public sealed class StarterTemplateIntegrationTests(StarterTemplateIntegrationTests.Installed installed) : IClassFixture<StarterTemplateIntegrationTests.Installed>
{
    /// <summary>A template store with the starter installed, which every test of the class reads.</summary>
    public sealed class Installed : IAsyncLifetime
    {
        public string Home { get; } = Directory.CreateTempSubdirectory("starter-home-").FullName;

        public async ValueTask InitializeAsync()
        {
            CommandResult result = await DotnetAsync(Home, null, "new", "install", Path.Combine(RepoRoot(), "templates", "starter"));
            Assert.True(result.ExitCode is 0, result.StandardOutput + result.StandardError);
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Home, true);
            return ValueTask.CompletedTask;
        }
    }

    private static readonly string[] LockFiles =
    [
        "src/App/packages.lock.json",
        "src/Core/packages.lock.json",
        "tests/Specs/packages.lock.json",
        "tests/Integration/packages.lock.json",
    ];

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Solution.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Solution.slnx is not above the test assembly");
    }

    private static async Task<CommandResult> DotnetAsync(string home, string? workingDirectory, params string[] arguments)
        => await new CommandRunner().RunAsync(new CommandSpec
        {
            Command = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = new() { ["DOTNET_CLI_HOME"] = home, ["DOTNET_NOLOGO"] = "1" },
            Timeout = TimeSpan.FromMinutes(5),
        }, TestContext.Current.CancellationToken);

    /// <summary>Runs a body over the installed template and a fresh parent directory for the copy, and removes the parent at its end.</summary>
    private async Task WithTemplateAsync(Func<string, string, Task> body)
    {
        await Cleanup.RunAsync(async cleanup =>
        {
            string parent = Directory.CreateTempSubdirectory("starter-copy-").FullName;
            cleanup.Add($"delete the directory {parent}", () => Directory.Delete(parent, true));
            await body(installed.Home, parent);
        });
    }

    private static async Task<string> NewCopyAsync(string home, string parent)
    {
        string copy = Path.Combine(parent, "copy");
        CommandResult created = await DotnetAsync(home, null, "new", "factory-starter", "-o", copy);
        Assert.True(created.ExitCode is 0, created.StandardOutput + created.StandardError);
        return copy;
    }

    private static async Task<string> RestoredCopyAsync(string home, string parent)
    {
        string copy = await NewCopyAsync(home, parent);
        CommandResult restore = await DotnetAsync(home, copy, "restore", "--locked-mode");
        Assert.True(restore.ExitCode is 0, restore.StandardOutput + restore.StandardError);
        return copy;
    }

    [Fact]
    public async Task TheInstallExitsWith0AndListsTheTemplate()
    {
        await Cleanup.RunAsync(async cleanup =>
        {
            string home = Directory.CreateTempSubdirectory("starter-home-").FullName;
            cleanup.Add($"delete the directory {home}", () => Directory.Delete(home, true));
            CommandResult installed = await DotnetAsync(home, null, "new", "install", Path.Combine(RepoRoot(), "templates", "starter"));
            Assert.Equal(0, installed.ExitCode);
            CommandResult list = await DotnetAsync(home, null, "new", "list");
            Assert.Contains(list.StandardOutput.Split('\n'), line => line.Contains("dotnet-software-factory starter", StringComparison.Ordinal) && line.Contains("factory-starter", StringComparison.Ordinal));
            await DotnetAsync(home, null, "new", "uninstall", Path.Combine(RepoRoot(), "templates", "starter"));
            CommandResult after = await DotnetAsync(home, null, "new", "list");
            Assert.DoesNotContain("factory-starter", after.StandardOutput, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DotnetNewCreatesTheCopyOfTheStarterAndNoFileOfTheTemplateConfiguration()
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = Path.Combine(parent, "copy");
            CommandResult created = await DotnetAsync(home, null, "new", "factory-starter", "-o", copy);
            Assert.Equal(0, created.ExitCode);
            Assert.Contains("The template \"dotnet-software-factory starter\" was created successfully.", created.StandardOutput, StringComparison.Ordinal);
            string starter = Path.Combine(RepoRoot(), "templates", "starter");
            string[] want = [.. Directory.EnumerateFiles(starter, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(starter, path).Replace('\\', '/'))
                .Where(file => !file.StartsWith(".template.config/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];
            string[] have = [.. Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(copy, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)];
            Assert.Equal(want, have);
            foreach (string file in want)
            {
                if (file is "src/App/App.csproj")
                {
                    continue;
                }

                Assert.Equal(File.ReadAllBytes(Path.Combine(starter, file)), File.ReadAllBytes(Path.Combine(copy, file)));
            }
        });
    }

    [Theory]
    [InlineData("my-factory-repo", null, "my-factory-repo")]
    [InlineData("forecast.sample", null, "forecast.sample")]
    [InlineData("named", "other-name", "other-name")]
    public async Task TheCopysProgramTakesTheNameOfTheCopy(string directory, string? name, string expected)
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = Path.Combine(parent, directory);
            string[] arguments = name is null ? ["new", "factory-starter", "-o", copy] : ["new", "factory-starter", "-o", copy, "-n", name];
            CommandResult created = await DotnetAsync(home, null, arguments);
            Assert.Equal(0, created.ExitCode);
            string? found = System.Xml.Linq.XDocument.Load(Path.Combine(copy, "src", "App", "App.csproj")).Descendants("AssemblyName").Select(e => e.Value).FirstOrDefault();
            Assert.Equal(expected, found);
        });
    }

    [Fact]
    public async Task TheCopyRestoresBuildsFormatsAndTestsWithNoEdit()
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = await NewCopyAsync(home, parent);
            Dictionary<string, byte[]> before = LockFiles.ToDictionary(file => file, file => File.ReadAllBytes(Path.Combine(copy, file)));
            string[][] commands =
            [
                ["restore", "--locked-mode"],
                ["build", "--no-restore"],
                ["format", "--verify-no-changes", "--no-restore"],
            ];
            foreach (string[] command in commands)
            {
                CommandResult result = await DotnetAsync(home, copy, command);
                Assert.True(result.ExitCode is 0, string.Join(' ', command) + "\n" + result.StandardOutput + result.StandardError);
            }

            CommandResult test = await DotnetAsync(home, copy, "test", "--no-restore");
            Assert.True(test.ExitCode is 0, test.StandardOutput + test.StandardError);
            Assert.Matches(@"Failed:\s+0, Passed:\s+2, Skipped:\s+0", test.StandardOutput);
            foreach (string file in LockFiles)
            {
                Assert.Equal(before[file], File.ReadAllBytes(Path.Combine(copy, file)));
            }
        });
    }

    private static string? Outcome(Dictionary<string, string?> results, string scenario)
        => results.Single(pair => pair.Key.EndsWith("." + FeatureFiles.ComputeTestName(scenario), StringComparison.Ordinal)).Value;

    [Fact]
    public async Task TheCopysTestFailsWhenTheProgramThrowsAtItsStart()
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = await NewCopyAsync(home, parent);
            string[][] commands = [["restore", "--locked-mode"], ["build", "--no-restore"], ["build", "--no-restore", "-c", "Release"]];
            foreach (string[] command in commands)
            {
                CommandResult result = await DotnetAsync(home, copy, command);
                Assert.True(result.ExitCode is 0, string.Join(' ', command) + "\n" + result.StandardOutput + result.StandardError);
            }

            string program = Path.Combine(copy, "src", "App", "Program.cs");
            string source = File.ReadAllText(program);
            string edited = source.Replace("using IHost host", "if (args.Length > -1)\n{\n    throw new InvalidOperationException(\"the program fails at its start\");\n}\n\nusing IHost host", StringComparison.Ordinal);
            Assert.NotEqual(source, edited);
            File.WriteAllText(program, edited);

            string trx = "start.trx";
            CommandResult test = await DotnetAsync(home, copy, "test", "--no-restore", "-c", "Release", "tests/Specs/Specs.csproj", "--logger", "trx;LogFileName=" + trx);
            Assert.Equal(1, test.ExitCode);
            Assert.Matches(@"Failed:\s+1, Passed:\s+1, Skipped:\s+0", test.StandardOutput);
            var results = System.Xml.Linq.XDocument.Load(Directory.EnumerateFiles(copy, trx, SearchOption.AllDirectories).Single()).Descendants()
                .Where(e => e.Name.LocalName is "UnitTestResult")
                .ToDictionary(e => (string?)e.Attribute("testName") ?? "", e => (string?)e.Attribute("outcome"));
            Assert.Equal("Failed", Outcome(results, "The launched program greets by name"));
            Assert.Equal("Passed", Outcome(results, "The greeter greets by name"));
        });
    }

    [Fact]
    public async Task TheCopysProgramGreetsAda()
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = await RestoredCopyAsync(home, parent);
            CommandResult run = await DotnetAsync(home, copy, "run", "--no-restore", "--project", "src/App", "--", "Ada");
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("Hello, Ada!", run.StandardOutput.Trim());
        });
    }

    [Fact]
    public async Task TheCopysProgramGreetsTheWorldWithNoArgument()
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = await RestoredCopyAsync(home, parent);
            CommandResult run = await DotnetAsync(home, copy, "run", "--no-restore", "--project", "src/App");
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("Hello, world!", run.StandardOutput.Trim());
        });
    }

    [Fact]
    public async Task TheCopysIntegrationSuiteRunsWithNoTest()
    {
        await WithTemplateAsync(async (home, parent) =>
        {
            string copy = await RestoredCopyAsync(home, parent);
            CommandResult run = await DotnetAsync(home, copy, "run", "--no-restore", "--project", "tests/Integration");
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("Integration  Total: 0", run.StandardOutput, StringComparison.Ordinal);
        });
    }
}
