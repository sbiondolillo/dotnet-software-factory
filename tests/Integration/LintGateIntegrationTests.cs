using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> canned for <see cref="LintGate"/> and <see cref="Git.HasChangesAsync"/> hold against the real programs.</summary>
public sealed class LintGateIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:integration";
    private static readonly CommandRunner Runner = new();
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-lintgate-tree-");

    public void Dispose() => _tree.Delete(recursive: true);

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        CommandResult result = await Runner.RunAsync(
            new CommandSpec
            {
                Command = "git",
                Arguments = arguments,
                WorkingDirectory = directory,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = "Seed",
                    ["GIT_AUTHOR_EMAIL"] = "seed@example.com",
                    ["GIT_COMMITTER_NAME"] = "Seed",
                    ["GIT_COMMITTER_EMAIL"] = "seed@example.com",
                },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput;
    }

    private async Task InitAsync(bool project)
    {
        await GitAsync(_tree.FullName, "init");
        File.WriteAllText(Path.Combine(_tree.FullName, ".gitignore"), "bin/\nobj/\n");
        if (project)
        {
            File.WriteAllText(
                Path.Combine(_tree.FullName, "Probe.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
            File.WriteAllText(
                Path.Combine(_tree.FullName, "Greeter.cs"),
                "public static class Greeter\n{\npublic static string Greet() => \"hi\";\n}\n");
        }

        await GitAsync(_tree.FullName, "add", "-A");
        await GitAsync(_tree.FullName, "commit", "-m", "seed");
    }

    [Fact]
    public async Task GitStatusAnswersForANewFileAndACleanTree()
    {
        await InitAsync(project: true);
        var git = new Git(Runner);

        Assert.False(await git.HasChangesAsync(_tree.FullName, TestContext.Current.CancellationToken));
        File.WriteAllText(Path.Combine(_tree.FullName, "Other.cs"), "class Other {}\n");
        Assert.True(await git.HasChangesAsync(_tree.FullName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FormatCommitsOnceThenFindsNothingToCommit()
    {
        await InitAsync(project: true);
        await using Sandbox sandbox = await Sandbox.StartAsync(
            Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);
        var gate = new LintGate(sandbox, new Git(Runner));

        Assert.True(await gate.RunAsync(_tree.FullName, "Add a checkout total", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));
        Assert.Contains("    public static string Greet()", File.ReadAllText(Path.Combine(_tree.FullName, "Greeter.cs")));
        Assert.True(Directory.Exists(Path.Combine(_tree.FullName, "obj")));
        Assert.Equal(
            "Add a checkout total|Ada Lovelace|ada@example.com\n",
            await GitAsync(_tree.FullName, "log", "-1", "--format=%s|%an|%ae"));

        string head = await GitAsync(_tree.FullName, "rev-parse", "HEAD");
        Assert.False(await gate.RunAsync(_tree.FullName, "again", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));
        Assert.Equal(head, await GitAsync(_tree.FullName, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task FormatWithNoProjectThrowsWithExitCodeAndReason()
    {
        await InitAsync(project: false);
        await using Sandbox sandbox = await Sandbox.StartAsync(
            Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);
        var gate = new LintGate(sandbox, new Git(Runner));

        var exception = await Assert.ThrowsAsync<LintGateException>(
            () => gate.RunAsync(_tree.FullName, "m", "a", "a@example.com", TestContext.Current.CancellationToken));

        Assert.Contains("exit code 1", exception.Message);
        Assert.Contains("Could not find a MSBuild project file or solution file", exception.Message);
    }
}
