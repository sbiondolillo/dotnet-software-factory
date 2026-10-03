using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the starter template's canned answers are what the real dotnet gives: each test installs the template, makes a copy and runs the command of its row.</summary>
public sealed class StarterTemplateIntegrationTests
{
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

    /// <summary>Runs a body over a fresh template store and an installed template, and removes both at its end.</summary>
    private static async Task WithTemplateAsync(Func<string, string, Task> body, bool install = true)
    {
        string home = Directory.CreateTempSubdirectory("starter-home-").FullName;
        string parent = Directory.CreateTempSubdirectory("starter-copy-").FullName;
        try
        {
            if (install)
            {
                CommandResult installed = await DotnetAsync(home, null, "new", "install", Path.Combine(RepoRoot(), "templates", "starter"));
                Assert.True(installed.ExitCode is 0, installed.StandardOutput + installed.StandardError);
            }

            await body(home, parent);
        }
        finally
        {
            Directory.Delete(home, true);
            Directory.Delete(parent, true);
        }
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
        string home = Directory.CreateTempSubdirectory("starter-home-").FullName;
        try
        {
            CommandResult installed = await DotnetAsync(home, null, "new", "install", Path.Combine(RepoRoot(), "templates", "starter"));
            Assert.Equal(0, installed.ExitCode);
            CommandResult list = await DotnetAsync(home, null, "new", "list");
            Assert.Contains(list.StandardOutput.Split('\n'), line => line.Contains("dotnet-software-factory starter", StringComparison.Ordinal) && line.Contains("factory-starter", StringComparison.Ordinal));
            await DotnetAsync(home, null, "new", "uninstall", Path.Combine(RepoRoot(), "templates", "starter"));
            CommandResult after = await DotnetAsync(home, null, "new", "list");
            Assert.DoesNotContain("factory-starter", after.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, true);
        }
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
                Assert.Equal(File.ReadAllBytes(Path.Combine(starter, file)), File.ReadAllBytes(Path.Combine(copy, file)));
            }
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
            Assert.Matches(@"Failed:\s+0, Passed:\s+1, Skipped:\s+0", test.StandardOutput);
            foreach (string file in LockFiles)
            {
                Assert.Equal(before[file], File.ReadAllBytes(Path.Combine(copy, file)));
            }
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
