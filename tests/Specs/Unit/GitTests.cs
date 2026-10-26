using System.Runtime.Versioning;
using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task CloneThrowsAGitCommandExceptionNamingCloneWhenTheCommandFails()
    {
        Mock<CommandRunner> mock = NewMock(("clone", new CommandResult { ExitCode = 1, StandardError = "fatal: could not clone" }));
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CloneAsync("https://example.com/repo.git", "token", "/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("clone", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: could not clone", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAllThrowsNamingAddWhenGitAddFailsAndNeverRunsCommit()
    {
        var runs = new List<string>();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                runs.Add(spec.Arguments[0]);
                return Task.FromResult(new CommandResult { ExitCode = 1, StandardError = "fatal: add failed" });
            });
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/dir", "message", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));

        Assert.Contains("add", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: add failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["add"], runs);
    }

    [Fact]
    public async Task CommitAllThrowsNamingCommitWhenGitCommitFails()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(spec.Arguments[0] == "add"
                ? new CommandResult { ExitCode = 0 }
                : new CommandResult { ExitCode = 1, StandardError = "fatal: commit failed" }));
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/dir", "message", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));

        Assert.Contains("commit", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: commit failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PushThrowsAGitCommandExceptionNamingPushWhenTheCommandFails()
    {
        Mock<CommandRunner> mock = NewMock(("push", new CommandResult { ExitCode = 1, StandardError = "fatal: could not push" }));
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("branch", "/tmp/dir", "token", TestContext.Current.CancellationToken));

        Assert.Contains("push", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: could not push", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadHeadShaThrowsAGitCommandExceptionNamingRevParseWhenTheCommandFails()
    {
        Mock<CommandRunner> mock = NewMock(("rev-parse", new CommandResult { ExitCode = 1, StandardError = "fatal: bad revision" }));
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(() => git.ReadHeadShaAsync("/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("rev-parse", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: bad revision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListChangedFilesThrowsAGitCommandExceptionNamingDiffWhenTheCommandFails()
    {
        Mock<CommandRunner> mock = NewMock(("diff", new CommandResult { ExitCode = 1, StandardError = "fatal: unknown revision" }));
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ListChangedFilesAsync("no-such-base", "/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("diff", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: unknown revision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGitCommandExceptionMessageHoldsTheWholeMultiLineStandardError()
    {
        string standardError = "fatal: line one\nfatal: line two\nfatal: line three";
        Mock<CommandRunner> mock = NewMock(("push", new CommandResult { ExitCode = 1, StandardError = standardError }));
        var git = new Git(mock.Object);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("branch", "/tmp/dir", "token", TestContext.Current.CancellationToken));

        Assert.Contains(standardError, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadHeadShaTrimsTheTrailingNewlineGitPrints()
    {
        Mock<CommandRunner> mock = NewMock(("rev-parse", new CommandResult { ExitCode = 0, StandardOutput = "903c6f7ea009814ad6cff2d387717be8f34bab03\n" }));
        var git = new Git(mock.Object);

        string sha = await git.ReadHeadShaAsync("/tmp/dir", TestContext.Current.CancellationToken);

        Assert.Equal("903c6f7ea009814ad6cff2d387717be8f34bab03", sha);
    }

    [Fact]
    public async Task PushNeverPassesTheTokenAsAnArgument()
    {
        CommandSpec? recorded = null;
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                recorded = spec;
                return Task.FromResult(new CommandResult { ExitCode = 0 });
            });
        var git = new Git(mock.Object);

        await git.PushAsync("branch", "/tmp/dir", "super-secret-token", TestContext.Current.CancellationToken);

        Assert.NotNull(recorded);
        Assert.DoesNotContain("super-secret-token", recorded.Arguments);
    }

    [Fact]
    public async Task CommitAllSetsTheAuthorEnvironmentVariablesAlongsideTheCommitterOnes()
    {
        CommandSpec? commitSpec = null;
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                if (spec.Arguments[0] == "commit")
                {
                    commitSpec = spec;
                }

                return Task.FromResult(new CommandResult { ExitCode = 0 });
            });
        var git = new Git(mock.Object);

        await git.CommitAllAsync("/tmp/dir", "message", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken);

        Assert.NotNull(commitSpec);
        Assert.Equal("Ada Lovelace", commitSpec.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal("ada@example.com", commitSpec.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
        Assert.Equal("Ada Lovelace", commitSpec.EnvironmentVariables["GIT_COMMITTER_NAME"]);
        Assert.Equal("ada@example.com", commitSpec.EnvironmentVariables["GIT_COMMITTER_EMAIL"]);
    }

    [Fact]
    public async Task CommitAllLetsAnOperationCanceledExceptionFromAddReachTheCallerAndNeverRunsCommit()
    {
        var runs = new List<string>();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                runs.Add(spec.Arguments[0]);
                if (spec.Arguments[0] == "add")
                {
                    throw new OperationCanceledException();
                }

                return Task.FromResult(new CommandResult { ExitCode = 0 });
            });
        var git = new Git(mock.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => git.CommitAllAsync("/tmp/dir", "message", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));

        Assert.Equal(["add"], runs);
    }

    [Fact]
    public async Task CheckoutBranchLetsAnOperationCanceledExceptionReachTheCallerUnwrapped()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec _, CancellationToken _) => throw new OperationCanceledException());
        var git = new Git(mock.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => git.CheckoutBranchAsync("branch", "/tmp/dir", TestContext.Current.CancellationToken));
    }

    private static Mock<CommandRunner> NewMock(params (string Subcommand, CommandResult Result)[] configured)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                string subcommand = spec.Arguments.Count > 0 ? spec.Arguments[0] : "";
                foreach ((string configuredSubcommand, CommandResult result) in configured)
                {
                    if (configuredSubcommand == subcommand)
                    {
                        return Task.FromResult(result);
                    }
                }

                return Task.FromResult(new CommandResult { ExitCode = 0 });
            });

        return mock;
    }

    [Theory]
    [InlineData("?? Other.cs\n", true)]
    [InlineData(" M A.cs\n?? B.cs\n", true)]
    [InlineData("", false)]
    public async Task HasChangesReadsTheStatusPorcelainOutput(string output, bool expected)
    {
        var specs = new List<CommandSpec>();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                specs.Add(spec);
                return Task.FromResult(new CommandResult { StandardOutput = output });
            });

        bool result = await new Git(mock.Object).HasChangesAsync("/tmp/dir", TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        CommandSpec single = Assert.Single(specs);
        Assert.Equal("git", single.Command);
        Assert.Equal(["status", "--porcelain"], single.Arguments);
        Assert.Equal("/tmp/dir", single.WorkingDirectory);
    }

    [Fact]
    public async Task HasChangesThrowsWhenStatusFails()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 128, StandardError = "fatal: not a repo" });

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => new Git(mock.Object).HasChangesAsync("/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasChangesRunsInAWorkTreeWhoseOnlyGitIsItsOwn()
    {
        DirectoryInfo tree = Directory.CreateTempSubdirectory("factory-git-tests-");
        try
        {
            Directory.CreateDirectory(Path.Combine(tree.FullName, ".git", "modules", "lib", ".git"));
            Directory.CreateDirectory(Path.Combine(tree.FullName, "src", ".github"));
            await File.WriteAllTextAsync(Path.Combine(tree.FullName, ".gitignore"), "bin/\n", TestContext.Current.CancellationToken);
            var mock = new Mock<CommandRunner>(MockBehavior.Strict);
            mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CommandResult { ExitCode = 0 });

            Assert.False(await new Git(mock.Object).HasChangesAsync(tree.FullName, TestContext.Current.CancellationToken));
        }
        finally
        {
            tree.Delete(recursive: true);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ANestedGitAndAnUnreadableDirectoryAreEachNamedAndNoCommandRuns()
    {
        DirectoryInfo tree = Directory.CreateTempSubdirectory("factory-git-tests-");
        string locked = Path.Combine(tree.FullName, "locked");
        try
        {
            Directory.CreateDirectory(Path.Combine(tree.FullName, "a", "b", ".git"));
            Directory.CreateDirectory(locked);
            File.SetUnixFileMode(locked, UnixFileMode.UserExecute);
            Assert.SkipWhen(Readable(locked), "The process reads a directory with no read permission, as root does.");
            var mock = new Mock<CommandRunner>(MockBehavior.Strict);

            GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
                () => new Git(mock.Object).HasChangesAsync(tree.FullName, TestContext.Current.CancellationToken));

            Assert.Contains("status", exception.Message, StringComparison.Ordinal);
            Assert.Contains("a/b/.git", exception.Message, StringComparison.Ordinal);
            Assert.Contains("locked (unreadable)", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            tree.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CloneLeavesADirectoryWithNoGitDirectoryAsItIs()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"factory-git-tests-{Guid.NewGuid():N}");
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0 });

        await new Git(mock.Object).CloneAsync("https://example.com/repo.git", "token", directory, TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task CloneStartsTheLineOnANewLineWhenTheExcludeFileEndsWithNone()
    {
        DirectoryInfo tree = Directory.CreateTempSubdirectory("factory-git-tests-");
        try
        {
            string exclude = Path.Combine(Directory.CreateDirectory(Path.Combine(tree.FullName, ".git", "info")).FullName, "exclude");
            await File.WriteAllTextAsync(exclude, "*.swp", TestContext.Current.CancellationToken);
            var mock = new Mock<CommandRunner>(MockBehavior.Strict);
            mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CommandResult { ExitCode = 0 });

            await new Git(mock.Object).CloneAsync("https://example.com/repo.git", "token", tree.FullName, TestContext.Current.CancellationToken);

            Assert.Equal("*.swp\n.factory/\n", await File.ReadAllTextAsync(exclude, TestContext.Current.CancellationToken));
        }
        finally
        {
            tree.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AFileUnderFactoryInARealCloneIsNoChangeAndNoCommitHoldsIt()
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("factory-git-tests-");
        try
        {
            var runner = new CommandRunner();
            string origin = Directory.CreateDirectory(Path.Combine(scratch.FullName, "origin")).FullName;
            string clone = Path.Combine(scratch.FullName, "clone");
            await RunGitAsync(runner, origin, "init", "--quiet");
            await File.WriteAllTextAsync(Path.Combine(origin, ".gitignore"), "bin/\n", TestContext.Current.CancellationToken);
            await RunGitAsync(runner, origin, "add", "-A");
            await RunGitAsync(runner, origin, "commit", "--quiet", "-m", "Start");
            var git = new Git(runner);

            await git.CloneAsync(origin, "token", clone, TestContext.Current.CancellationToken);
            Directory.CreateDirectory(Path.Combine(clone, ".factory"));
            await File.WriteAllTextAsync(Path.Combine(clone, ".factory", "plan.md"), "# Plan\n", TestContext.Current.CancellationToken);

            Assert.False(await git.HasChangesAsync(clone, TestContext.Current.CancellationToken));

            await File.WriteAllTextAsync(Path.Combine(clone, "README.md"), "# Readme\n", TestContext.Current.CancellationToken);
            await git.CommitAllAsync(clone, "Add README", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken);
            CommandResult files = await RunGitAsync(runner, clone, "ls-tree", "-r", "--name-only", "HEAD");

            Assert.Equal([".gitignore", "README.md"], files.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static async Task<CommandResult> RunGitAsync(CommandRunner runner, string directory, params string[] arguments)
    {
        CommandResult result = await runner.RunAsync(
            new CommandSpec
            {
                Command = "git",
                Arguments = arguments,
                WorkingDirectory = directory,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = "Ada Lovelace",
                    ["GIT_AUTHOR_EMAIL"] = "ada@example.com",
                    ["GIT_COMMITTER_NAME"] = "Ada Lovelace",
                    ["GIT_COMMITTER_EMAIL"] = "ada@example.com",
                },
            },
            TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode is 0, result.StandardError);
        return result;
    }

    private static bool Readable(string directory)
    {
        try
        {
            Directory.GetFileSystemEntries(directory);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
