using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> relies on for the starter template hold against the real <c>dotnet new</c>.</summary>
public sealed class StarterTemplateIntegrationTests
{
    private static readonly CommandRunner Runner = new();

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private static Task<CommandResult> DotnetAsync(string home, string directory, params string[] arguments) =>
        Runner.RunAsync(
            new CommandSpec
            {
                Command = "dotnet",
                Arguments = arguments,
                WorkingDirectory = directory,
                EnvironmentVariables = new Dictionary<string, string> { ["DOTNET_CLI_HOME"] = home, ["DOTNET_NOLOGO"] = "1" },
            },
            TestContext.Current.CancellationToken);

    private static void CopyTree(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [Fact]
    public async Task InstallExitsWithZeroAndListsTheTemplateAndAReinstallExitsWith106()
    {
        string home = Directory.CreateTempSubdirectory("factory-starter-home-").FullName;
        try
        {
            string starter = Path.Combine(RepoRoot(), "templates/starter");
            CommandResult install = await DotnetAsync(home, RepoRoot(), "new", "install", starter);
            CommandResult list = await DotnetAsync(home, RepoRoot(), "new", "list");
            CommandResult again = await DotnetAsync(home, RepoRoot(), "new", "install", starter);
            CommandResult uninstall = await DotnetAsync(home, RepoRoot(), "new", "uninstall", starter);
            CommandResult listAfter = await DotnetAsync(home, RepoRoot(), "new", "list");

            Assert.Equal(0, install.ExitCode);
            Assert.Contains(list.StandardOutput.Split('\n'), line => line.Contains("dotnet-software-factory starter", StringComparison.Ordinal) && line.Contains("factory-starter", StringComparison.Ordinal));
            Assert.Equal(106, again.ExitCode);
            Assert.Contains("is already installed.", again.StandardOutput + again.StandardError, StringComparison.Ordinal);
            Assert.Equal(0, uninstall.ExitCode);
            Assert.DoesNotContain("factory-starter", listAfter.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task CreateExitsWithZeroAndCreateWithNoTemplateExitsWith103()
    {
        string home = Directory.CreateTempSubdirectory("factory-starter-home-").FullName;
        string copy = Directory.CreateTempSubdirectory("factory-starter-copy-").FullName;
        try
        {
            CommandResult none = await DotnetAsync(home, copy, "new", "factory-starter");
            Assert.Equal(103, none.ExitCode);
            Assert.Contains("No templates or subcommands found matching: 'factory-starter'.", none.StandardOutput + none.StandardError, StringComparison.Ordinal);

            await DotnetAsync(home, copy, "new", "install", Path.Combine(RepoRoot(), "templates/starter"));
            CommandResult created = await DotnetAsync(home, copy, "new", "factory-starter");
            Assert.Equal(0, created.ExitCode);
            Assert.Contains("The template \"dotnet-software-factory starter\" was created successfully.", created.StandardOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(copy, "src/App/packages.lock.json")));
            Assert.False(Directory.Exists(Path.Combine(copy, ".template.config")));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public async Task ATemplateWithNoSourcesListLeavesOutThePackagesLockFiles()
    {
        string home = Directory.CreateTempSubdirectory("factory-starter-home-").FullName;
        string template = Directory.CreateTempSubdirectory("factory-starter-template-").FullName;
        string copy = Directory.CreateTempSubdirectory("factory-starter-copy-").FullName;
        try
        {
            CopyTree(Path.Combine(RepoRoot(), "templates/starter"), template);
            string config = Path.Combine(template, ".template.config/template.json");
            File.WriteAllText(config, File.ReadAllText(config).Split("\"sources\"")[0].TrimEnd().TrimEnd(',') + "\n}\n");
            await DotnetAsync(home, copy, "new", "install", template);
            CommandResult created = await DotnetAsync(home, copy, "new", "factory-starter");

            Assert.Equal(0, created.ExitCode);
            Assert.False(File.Exists(Path.Combine(copy, "src/App/packages.lock.json")));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
            Directory.Delete(template, recursive: true);
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public async Task TheCopyRestoresBuildsFormatsTestsAndRunsAndKeepsItsLockFiles()
    {
        string home = Directory.CreateTempSubdirectory("factory-starter-home-").FullName;
        string copy = Directory.CreateTempSubdirectory("factory-starter-copy-").FullName;
        try
        {
            string starter = Path.Combine(RepoRoot(), "templates/starter");
            await DotnetAsync(home, copy, "new", "install", starter);
            await DotnetAsync(home, copy, "new", "factory-starter");

            string[][] commands =
            [
                ["restore", "--locked-mode"],
                ["build", "--no-restore"],
                ["format", "--verify-no-changes", "--no-restore"],
                ["test", "--no-restore"],
            ];
            foreach (string[] command in commands)
            {
                CommandResult result = await DotnetAsync(home, copy, command);
                Assert.True(result.ExitCode is 0, $"{string.Join(' ', command)}: {result.StandardOutput}");
                if (command[0] is "test")
                {
                    Assert.Matches(@"Passed:\s+1\b", result.StandardOutput);
                    Assert.Matches(@"Failed:\s+0\b", result.StandardOutput);
                    Assert.Matches(@"Skipped:\s+0\b", result.StandardOutput);
                }
            }

            foreach (string project in new[] { "src/App", "src/Core", "tests/Specs", "tests/Integration" })
            {
                Assert.Equal(File.ReadAllBytes(Path.Combine(starter, project, "packages.lock.json")), File.ReadAllBytes(Path.Combine(copy, project, "packages.lock.json")));
            }

            CommandResult ada = await DotnetAsync(home, copy, "run", "--no-build", "--project", "src/App", "--", "Ada");
            CommandResult none = await DotnetAsync(home, copy, "run", "--no-build", "--project", "src/App");
            CommandResult help = await DotnetAsync(home, copy, "run", "--no-build", "--project", "src/App", "--", "--help");
            CommandResult extra = await DotnetAsync(home, copy, "run", "--no-build", "--project", "src/App", "--", "Ada", "Lovelace");
            CommandResult integration = await DotnetAsync(home, copy, "run", "--no-restore", "--project", "tests/Integration");

            Assert.Equal("Hello, Ada!", ada.StandardOutput.Trim());
            Assert.Equal("Hello, world!", none.StandardOutput.Trim());
            Assert.Contains("[default: world]", help.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(1, extra.ExitCode);
            Assert.Contains("Unrecognized command or argument 'Lovelace'.", extra.StandardError, StringComparison.Ordinal);
            Assert.Equal(0, integration.ExitCode);
            Assert.Contains("Total: 0", integration.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreWithTheLockFilesRemovedWritesTheSameFiles()
    {
        string home = Directory.CreateTempSubdirectory("factory-starter-home-").FullName;
        string copy = Directory.CreateTempSubdirectory("factory-starter-copy-").FullName;
        try
        {
            string starter = Path.Combine(RepoRoot(), "templates/starter");
            CopyTree(starter, copy);
            string[] projects = ["src/App", "src/Core", "tests/Specs", "tests/Integration"];
            foreach (string project in projects)
            {
                File.Delete(Path.Combine(copy, project, "packages.lock.json"));
            }

            CommandResult restore = await DotnetAsync(home, copy, "restore");

            Assert.True(restore.ExitCode is 0, restore.StandardOutput);
            foreach (string project in projects)
            {
                Assert.Equal(File.ReadAllBytes(Path.Combine(starter, project, "packages.lock.json")), File.ReadAllBytes(Path.Combine(copy, project, "packages.lock.json")));
            }
        }
        finally
        {
            Directory.Delete(home, recursive: true);
            Directory.Delete(copy, recursive: true);
        }
    }
}
