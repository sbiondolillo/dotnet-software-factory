using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task AFailedCloneThrowsNamingCloneAndHoldingTheStandardError()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        Fail(mock, 1, "fatal: could not read from remote");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CloneAsync("sbiondolillo", "factory-scratch", "token", "/tmp/factory-clone"));

        Assert.Equal("clone", exception.Subcommand);
        Assert.Equal("fatal: could not read from remote", exception.StandardError);
        Assert.Contains("clone", exception.Message);
        Assert.Contains("fatal: could not read from remote", exception.Message);
    }

    [Fact]
    public async Task AFailedPushThrowsNamingPushAndHoldingTheStandardError()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        Fail(mock, 128, "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git'");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("/tmp/factory-clone", "feature-1", "token"));

        Assert.Equal("push", exception.Subcommand);
        Assert.Equal("fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git'", exception.StandardError);
        Assert.Contains("push", exception.Message);
    }

    [Fact]
    public async Task AFailedAddThrowsNamingAdd()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        Fail(mock, 1, "fatal: add failed");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/factory-clone", "message", "Ada Lovelace", "ada@example.com"));

        Assert.Equal("add", exception.Subcommand);
        Assert.Equal("fatal: add failed", exception.StandardError);
        Assert.Contains("add", exception.Message);
    }

    [Fact]
    public async Task AFailedCommitThrowsNamingCommitAndHoldingTheStandardError()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        mock
            .SetupSequence(run => run.RunAsync(It.IsAny<CommandSpec>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0 })
            .ReturnsAsync(new CommandResult { ExitCode = 1, StandardError = "fatal: nothing to commit" });

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/factory-clone", "message", "Ada Lovelace", "ada@example.com"));

        Assert.Equal("commit", exception.Subcommand);
        Assert.Equal("fatal: nothing to commit", exception.StandardError);
        Assert.Contains("commit", exception.Message);
    }

    [Fact]
    public async Task AFailedRevParseThrowsNamingRevParse()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        Fail(mock, 128, "fatal: not a git repository");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ReadHeadShaAsync("/tmp/factory-clone"));

        Assert.Equal("rev-parse", exception.Subcommand);
        Assert.Contains("rev-parse", exception.Message);
    }

    [Fact]
    public async Task AFailedDiffThrowsNamingDiffAndHoldingTheStandardError()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        Fail(mock, 128, "fatal: unknown revision 'no-such-base'");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ListChangedFilesAsync("/tmp/factory-clone", "no-such-base"));

        Assert.Equal("diff", exception.Subcommand);
        Assert.Equal("fatal: unknown revision 'no-such-base'", exception.StandardError);
        Assert.Contains("diff", exception.Message);
    }

    [Fact]
    public async Task ANonZeroExitCodeOtherThanOneAlsoThrows()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        Fail(mock, 128, "fatal: remote repository does not exist");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CloneAsync("sbiondolillo", "factory-scratch", "token", "/tmp/factory-clone"));

        Assert.Equal("clone", exception.Subcommand);
        Assert.Equal("fatal: remote repository does not exist", exception.StandardError);
    }

    [Fact]
    public async Task TheHeadShaIsTheFirstFortyCharactersOfGitStdout()
    {
        (Git git, Mock<CommandRunner> mock) = MakeGit();
        mock
            .Setup(run => run.RunAsync(It.IsAny<CommandSpec>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, StandardOutput = "903c6f7ea009814ad6cff2d387717be8f34bab03\n" });

        string headSha = await git.ReadHeadShaAsync("/tmp/factory-clone");

        Assert.Equal("903c6f7ea009814ad6cff2d387717be8f34bab03", headSha);
    }

    private static (Git git, Mock<CommandRunner> mock) MakeGit()
    {
        var mock = new Mock<CommandRunner>();
        return (new Git(mock.Object), mock);
    }

    private static void Fail(Mock<CommandRunner> mock, int exitCode, string standardError)
        => mock
            .Setup(run => run.RunAsync(It.IsAny<CommandSpec>()))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardError = standardError });
}
