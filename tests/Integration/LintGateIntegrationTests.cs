using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> canned for <see cref="LintGate"/> and the change query of <see cref="Git"/> still hold.</summary>
public sealed class LintGateIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:lintgate-integration";
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

    private static async Task<CommandResult> GitRawAsync(string directory, params string[] arguments) =>
        await Runner.RunAsync(
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

    private async Task InitAsync(string greeter)
    {
        await GitRawAsync(_tree.FullName, "init");
        await File.WriteAllTextAsync(Path.Combine(_tree.FullName, ".gitignore"), "bin/\nobj/\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(_tree.FullName, "Probe.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tree.FullName, "Greeter.cs"), greeter, TestContext.Current.CancellationToken);
        await GitRawAsync(_tree.FullName, "add", "-A");
        await GitRawAsync(_tree.FullName, "commit", "-m", "seed");
    }

    private const string Unformatted = "public class Greeter\n{\npublic void Greet() { }\n}\n";
    private const string Formatted = "public class Greeter\n{\n    public void Greet() { }\n}\n";

    [Fact]
    public async Task TheChangeQueryIsTrueForANewFileAndFalseAfterTheCommit()
    {
        await InitAsync(Formatted);
        var git = new Git(Runner);
        Assert.False(await git.HasChangesAsync(_tree.FullName, TestContext.Current.CancellationToken));

        await File.WriteAllTextAsync(Path.Combine(_tree.FullName, "Other.cs"), "class Other { }\n", TestContext.Current.CancellationToken);
        Assert.True(await git.HasChangesAsync(_tree.FullName, TestContext.Current.CancellationToken));

        await git.CommitAllAsync(_tree.FullName, "add", "Ada", "ada@example.com", TestContext.Current.CancellationToken);
        Assert.False(await git.HasChangesAsync(_tree.FullName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FormatRewritesAnUnformattedFileAndLintGateCommitsIt()
    {
        await InitAsync(Unformatted);
        await new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), TestContext.Current.CancellationToken);
        await using Sandbox sandbox = await Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);
        var git = new Git(Runner);

        bool committed = await new LintGate(sandbox, git).RunAsync(
            _tree.FullName, "Format it", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken);

        Assert.True(committed);
        Assert.Contains("    public void Greet", await File.ReadAllTextAsync(Path.Combine(_tree.FullName, "Greeter.cs"), TestContext.Current.CancellationToken));
        CommandResult log = await GitRawAsync(_tree.FullName, "log", "-1", "--format=%s|%an|%ae");
        Assert.Equal("Format it|Ada Lovelace|ada@example.com", log.StandardOutput.Trim());
        Assert.False(await git.HasChangesAsync(_tree.FullName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFormattedTreeGetsNoCommitAndAMissingProjectThrows()
    {
        await InitAsync(Formatted);
        await new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), TestContext.Current.CancellationToken);
        await using Sandbox sandbox = await Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);
        var git = new Git(Runner);
        string head = await git.ReadHeadShaAsync(_tree.FullName, TestContext.Current.CancellationToken);

        Assert.False(await new LintGate(sandbox, git).RunAsync(_tree.FullName, "m", "n", "e@x", TestContext.Current.CancellationToken));
        Assert.Equal(head, await git.ReadHeadShaAsync(_tree.FullName, TestContext.Current.CancellationToken));

        File.Delete(Path.Combine(_tree.FullName, "Probe.csproj"));
        var exception = await Assert.ThrowsAsync<LintGateException>(
            () => new LintGate(sandbox, git).RunAsync(_tree.FullName, "m", "n", "e@x", TestContext.Current.CancellationToken));
        Assert.Contains("1", exception.Message);
        Assert.Contains("Could not find a MSBuild project file or solution file", exception.Message);
    }
}
