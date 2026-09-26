using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task AnUnknownBaseMakesListChangedFilesThrowNamingDiffAndHoldingTheStderr()
    {
        Git git = BuildGit("diff", new CommandResult
        {
            ExitCode = 128,
            StandardError = "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.",
        });

        GitException exception = await Assert.ThrowsAsync<GitException>(() => git.ListChangedFilesAsync("no-such-base"));

        Assert.Contains("diff", exception.Message);
        Assert.Contains("fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.", exception.Message);
    }

    [Fact]
    public async Task ARefusedPushThrowsNamingPushAndHoldingTheStderr()
    {
        Git git = BuildGit("push", new CommandResult
        {
            ExitCode = 1,
            StandardError = "! [rejected] factory/run -> factory/run (fetch first)",
        });

        GitException exception = await Assert.ThrowsAsync<GitException>(() => git.PushAsync("factory/run", "ghp_example"));

        Assert.Contains("push", exception.Message);
        Assert.Contains("! [rejected] factory/run -> factory/run (fetch first)", exception.Message);
    }

    [Fact]
    public async Task APushWithAnInvalidTokenThrowsNamingTheFailedAuthentication()
    {
        Git git = BuildGit("push", new CommandResult
        {
            ExitCode = 128,
            StandardError = "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'",
        });

        GitException exception = await Assert.ThrowsAsync<GitException>(() => git.PushAsync("factory/run", "invalid"));

        Assert.Contains("Authentication failed", exception.Message);
    }

    [Fact]
    public async Task AnyOtherNonZeroExitThrowsNamingTheSubcommandAndHoldingTheStderr()
    {
        Git git = BuildGit("clone", new CommandResult
        {
            ExitCode = 128,
            StandardError = "fatal: repository not found",
        });

        GitException exception = await Assert.ThrowsAsync<GitException>(
            () => git.CloneAsync("https://github.com/sbiondolillo/factory-scratch.git", "/tmp/factory-clone", "ghp_example"));

        Assert.Contains("clone", exception.Message);
        Assert.Contains("fatal: repository not found", exception.Message);
    }

    [Fact]
    public async Task TheCommitSpecCarriesTheAuthorNameAndEmailAlongsideTheCommitterPair()
    {
        List<CommandSpec> recorded = [];
        Git git = new Git(RecordingRunner(recorded));
        await git.CommitAsync("test commit", "Ada Lovelace", "ada@example.com");

        CommandSpec commitSpec = recorded.Single(spec => spec.Arguments[0] == "commit");

        Assert.Equal("Ada Lovelace", commitSpec.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal("ada@example.com", commitSpec.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
        Assert.Equal("Ada Lovelace", commitSpec.EnvironmentVariables["GIT_COMMITTER_NAME"]);
        Assert.Equal("ada@example.com", commitSpec.EnvironmentVariables["GIT_COMMITTER_EMAIL"]);
    }

    [Fact]
    public async Task AHeadReadWhoseStandardOutputEndsInALineFeedReturnsTheBareSha()
    {
        Git git = BuildGit("rev-parse", new CommandResult
        {
            ExitCode = 0,
            StandardOutput = "903c6f7ea009814ad6cff2d387717be8f34bab03\n",
        });

        string headSha = await git.ReadHeadShaAsync();

        Assert.Equal("903c6f7ea009814ad6cff2d387717be8f34bab03", headSha);
    }

    private static Git BuildGit(string subcommand, CommandResult canned)
    {
        List<CommandSpec> recorded = [];
        Mock<CommandRunner> mock = new();
        mock
            .Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(spec => recorded.Add(spec))
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
        mock
            .Setup(runner => runner.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Count > 0 && spec.Arguments[0] == subcommand)))
            .Callback<CommandSpec>(spec => recorded.Add(spec))
            .ReturnsAsync(canned);
        return new Git(mock.Object);
    }

    private static CommandRunner RecordingRunner(List<CommandSpec> recorded)
    {
        Mock<CommandRunner> mock = new();
        mock
            .Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(spec => recorded.Add(spec))
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
        return mock.Object;
    }
}
