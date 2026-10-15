using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the real model removes the feature file of behaviour an issue replaces, in a copy of the starter.</summary>
public sealed class PlanPhaseIntegrationTests
{
    private const string Tag = "factory-sandbox:integration";
    private const string Greeting = "specs/features/greeting.feature";

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Solution.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Solution.slnx is not above the test assembly");
    }

    private static async Task<CommandResult> DotnetAsync(string home, params string[] arguments)
        => await new CommandRunner().RunAsync(new CommandSpec
        {
            Command = "dotnet",
            Arguments = arguments,
            EnvironmentVariables = new() { ["DOTNET_CLI_HOME"] = home, ["DOTNET_NOLOGO"] = "1" },
            Timeout = TimeSpan.FromMinutes(5),
        }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ThePlanPhaseRemovesTheGreetingFileWhenTheIssueReplacesTheGreeting()
    {
        await Cleanup.RunAsync(async cleanup =>
        {
            string home = Directory.CreateTempSubdirectory("plan-home-").FullName;
            cleanup.Add($"delete the directory {home}", () => Directory.Delete(home, true));
            string parent = Directory.CreateTempSubdirectory("plan-copy-").FullName;
            cleanup.Add($"delete the directory {parent}", () => Directory.Delete(parent, true));
            string copy = Path.Combine(parent, "copy");
            CommandResult installed = await DotnetAsync(home, "new", "install", Path.Combine(RepoRoot(), "templates", "starter"));
            Assert.True(installed.ExitCode is 0, installed.StandardOutput + installed.StandardError);
            CommandResult created = await DotnetAsync(home, "new", "factory-starter", "-o", copy);
            Assert.True(created.ExitCode is 0, created.StandardOutput + created.StandardError);
            Assert.True(File.Exists(Path.Combine(copy, Greeting)));

            await using Sandbox sandbox = await Sandbox.StartAsync(new CommandRunner(), Tag, RepoRoot(), copy, TestContext.Current.CancellationToken);
            PlanPhaseResult result = await new PlanPhase(new ClaudeCodeAgent(sandbox)).RunAsync(
                "The program converts a length from miles to kilometres",
                "The console program converts a length from miles to kilometres. A person runs it with one argument, a number of miles, and it prints the number of kilometres as one line. The conversion replaces the greeting of the starter. The greeting's code, its feature files, including the one that launches the program, and its tests leave the repo.",
                "claude-sonnet-5-5",
                50,
                copy,
                TestContext.Current.CancellationToken);

            Assert.True(result.Passed, string.Join("; ", result.Failures));
            Assert.False(File.Exists(Path.Combine(copy, Greeting)));
            Assert.Contains(
                Directory.EnumerateFiles(Path.Combine(copy, "specs", "features"), "*.feature", SearchOption.AllDirectories),
                file => !file.EndsWith("launch-program.feature", StringComparison.Ordinal));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(Path.Combine(copy, "specs", "features"), "*.feature", SearchOption.AllDirectories),
                file => File.ReadAllText(file).Contains("Hello, Ada!", StringComparison.Ordinal));
        });
    }
}
