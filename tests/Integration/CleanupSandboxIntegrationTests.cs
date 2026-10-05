using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> canned for the cleanup run's check command hold in the real sandbox.</summary>
public sealed class CleanupSandboxIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:integration";
    private static readonly CommandRunner Runner = new();
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-cleanup-tree-");

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

    private async Task<Sandbox> StartAsync()
    {
        await new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), TestContext.Current.CancellationToken).ConfigureAwait(false);
        return await Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private async Task GitAsync(params string[] arguments)
    {
        CommandResult result = await Runner.RunAsync(
            new CommandSpec
            {
                Command = "git",
                Arguments = arguments,
                WorkingDirectory = _tree.FullName,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = "t",
                    ["GIT_AUTHOR_EMAIL"] = "t@example.com",
                    ["GIT_COMMITTER_NAME"] = "t",
                    ["GIT_COMMITTER_EMAIL"] = "t@example.com",
                },
            },
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task AWorktreeCopyOutsideWorkHoldsTheHeadFilesRunsTheCommandInItAndIsRemoved()
    {
        await GitAsync("init", "--quiet");
        await File.WriteAllTextAsync(Path.Combine(_tree.FullName, "tracked.txt"), "head", TestContext.Current.CancellationToken);
        await GitAsync("add", "-A");
        await GitAsync("commit", "--quiet", "-m", "head");
        await File.WriteAllTextAsync(Path.Combine(_tree.FullName, "untracked.txt"), "x", TestContext.Current.CancellationToken);

        await using Sandbox sandbox = await StartAsync();
        string copy = $"/tmp/check-{Guid.NewGuid():N}";
        CancellationToken token = TestContext.Current.CancellationToken;

        CommandResult add = await sandbox.RunAsync("git", ["worktree", "add", "--detach", copy, "HEAD"], [], token);
        try
        {
            Assert.Equal(0, add.ExitCode);
            CommandResult run = await sandbox.RunAsync("sh", ["-c", "pwd; cat tracked.txt; test ! -e untracked.txt"], [], copy, token);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal($"{copy}\nhead", run.StandardOutput);
        }
        finally
        {
            CommandResult remove = await sandbox.RunAsync("git", ["worktree", "remove", "--force", copy], [], token);
            Assert.Equal(0, remove.ExitCode);
        }

        Assert.NotEqual(0, (await sandbox.RunAsync("test", ["-e", copy], [], token)).ExitCode);
    }

    [Fact]
    public async Task ShellExits126ForANonExecutableFileAnd127ForAMissingProgramWithAStandardError()
    {
        await File.WriteAllTextAsync(Path.Combine(_tree.FullName, "plain"), "echo hi\n", TestContext.Current.CancellationToken);
        await using Sandbox sandbox = await StartAsync();
        CancellationToken token = TestContext.Current.CancellationToken;

        CommandResult denied = await sandbox.RunAsync("sh", ["-c", "./plain"], [], token);
        Assert.Equal(126, denied.ExitCode);
        Assert.NotEmpty(denied.StandardError);

        CommandResult missing = await sandbox.RunAsync("sh", ["-c", "factory-no-such-program"], [], token);
        Assert.Equal(127, missing.ExitCode);
        Assert.NotEmpty(missing.StandardError);
    }
}
