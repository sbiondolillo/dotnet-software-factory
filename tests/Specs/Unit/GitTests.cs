using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task CloneThrowsAGitCommandExceptionNamingCloneWhenTheCommandFails()
    {
        var mock = NewMock(("clone", new CommandResult { ExitCode = 1, StandardError = "fatal: could not clone" }));
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CloneAsync("https://example.com/repo.git", "token", "/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("clone", exception.Message);
        Assert.Contains("fatal: could not clone", exception.Message);
    }

    [Fact]
    public async Task CommitAllThrowsNamingAddWhenGitAddFailsAndNeverRunsCommit()
    {
        var runs = new List<string>();
        var mock = new Mock<CommandRunner>();
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                runs.Add(spec.Arguments[0]);
                return Task.FromResult(new CommandResult { ExitCode = 1, StandardError = "fatal: add failed" });
            });
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/dir", "message", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));

        Assert.Contains("add", exception.Message);
        Assert.Contains("fatal: add failed", exception.Message);
        Assert.Equal(["add"], runs);
    }

    [Fact]
    public async Task CommitAllThrowsNamingCommitWhenGitCommitFails()
    {
        var mock = new Mock<CommandRunner>();
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(spec.Arguments[0] == "add"
                ? new CommandResult { ExitCode = 0 }
                : new CommandResult { ExitCode = 1, StandardError = "fatal: commit failed" }));
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/dir", "message", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken));

        Assert.Contains("commit", exception.Message);
        Assert.Contains("fatal: commit failed", exception.Message);
    }

    [Fact]
    public async Task PushThrowsAGitCommandExceptionNamingPushWhenTheCommandFails()
    {
        var mock = NewMock(("push", new CommandResult { ExitCode = 1, StandardError = "fatal: could not push" }));
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("branch", "/tmp/dir", "token", TestContext.Current.CancellationToken));

        Assert.Contains("push", exception.Message);
        Assert.Contains("fatal: could not push", exception.Message);
    }

    [Fact]
    public async Task ReadHeadShaThrowsAGitCommandExceptionNamingRevParseWhenTheCommandFails()
    {
        var mock = NewMock(("rev-parse", new CommandResult { ExitCode = 1, StandardError = "fatal: bad revision" }));
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(() => git.ReadHeadShaAsync("/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("rev-parse", exception.Message);
        Assert.Contains("fatal: bad revision", exception.Message);
    }

    [Fact]
    public async Task ListChangedFilesThrowsAGitCommandExceptionNamingDiffWhenTheCommandFails()
    {
        var mock = NewMock(("diff", new CommandResult { ExitCode = 1, StandardError = "fatal: unknown revision" }));
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ListChangedFilesAsync("no-such-base", "/tmp/dir", TestContext.Current.CancellationToken));

        Assert.Contains("diff", exception.Message);
        Assert.Contains("fatal: unknown revision", exception.Message);
    }

    [Fact]
    public async Task AGitCommandExceptionMessageHoldsTheWholeMultiLineStandardError()
    {
        string standardError = "fatal: line one\nfatal: line two\nfatal: line three";
        var mock = NewMock(("push", new CommandResult { ExitCode = 1, StandardError = standardError }));
        var git = new Git(mock.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("branch", "/tmp/dir", "token", TestContext.Current.CancellationToken));

        Assert.Contains(standardError, exception.Message);
    }

    [Fact]
    public async Task ReadHeadShaTrimsTheTrailingNewlineGitPrints()
    {
        var mock = NewMock(("rev-parse", new CommandResult { ExitCode = 0, StandardOutput = "903c6f7ea009814ad6cff2d387717be8f34bab03\n" }));
        var git = new Git(mock.Object);

        string sha = await git.ReadHeadShaAsync("/tmp/dir", TestContext.Current.CancellationToken);

        Assert.Equal("903c6f7ea009814ad6cff2d387717be8f34bab03", sha);
    }

    [Fact]
    public async Task PushNeverPassesTheTokenAsAnArgument()
    {
        CommandSpec? recorded = null;
        var mock = new Mock<CommandRunner>();
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
        var mock = new Mock<CommandRunner>();
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
        var mock = new Mock<CommandRunner>();
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
        var mock = new Mock<CommandRunner>();
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec _, CancellationToken _) => throw new OperationCanceledException());
        var git = new Git(mock.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => git.CheckoutBranchAsync("branch", "/tmp/dir", TestContext.Current.CancellationToken));
    }

    private static Mock<CommandRunner> NewMock(params (string Subcommand, CommandResult Result)[] configured)
    {
        var mock = new Mock<CommandRunner>();
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
}
