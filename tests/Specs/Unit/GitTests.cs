using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task APushHoldsTheTokenInNoArgument()
    {
        (Git git, List<CommandSpec> specs) = MockedGit(0, "", "");

        await git.PushAsync("/tmp/factory-clone-1", "factory/run", "test-token");

        Assert.DoesNotContain("test-token", specs[^1].Arguments);
    }

    [Fact]
    public async Task AFailingCloneThrowsACommandExceptionThatNamesTheCloneAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGit(128, "", "fatal: repository 'https://github.com/sbiondolillo/factory-scratch.git/' not found");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CloneAsync("https://github.com/sbiondolillo/factory-scratch.git", "test-token", "/tmp/factory-clone-1"));

        Assert.Contains("clone", exception.Message);
        Assert.Contains("fatal: repository 'https://github.com/sbiondolillo/factory-scratch.git/' not found", exception.Message);
    }

    [Fact]
    public async Task AFailingCheckoutThrowsACommandExceptionThatNamesTheCheckoutAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGit(128, "", "fatal: a branch named 'factory/run' already exists");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CheckoutBranchAsync("/tmp/factory-clone-1", "factory/run"));

        Assert.Contains("checkout", exception.Message);
        Assert.Contains("fatal: a branch named 'factory/run' already exists", exception.Message);
    }

    [Fact]
    public async Task AFailingAddThrowsACommandExceptionThatNamesTheAddAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGit(128, "", "fatal: not a git repository (or any of the parent directories): .git");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/factory-clone-1", "Do the work", "Factory Bot", "bot@factory.example"));

        Assert.Contains("add", exception.Message);
        Assert.Contains("fatal: not a git repository (or any of the parent directories): .git", exception.Message);
    }

    [Fact]
    public async Task AFailingCommitThrowsACommandExceptionThatNamesTheCommitAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGitThenFailing(1, "", "Aborting commit due to empty commit message");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAllAsync("/tmp/factory-clone-1", "Do the work", "Factory Bot", "bot@factory.example"));

        Assert.Contains("commit", exception.Message);
        Assert.Contains("Aborting commit due to empty commit message", exception.Message);
    }

    [Fact]
    public async Task AFailingHeadReadThrowsACommandExceptionThatNamesTheRevParseAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGit(128, "", "fatal: ambiguous argument 'HEAD': unknown revision");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ReadHeadShaAsync("/tmp/factory-clone-1"));

        Assert.Contains("rev-parse", exception.Message);
        Assert.Contains("fatal: ambiguous argument 'HEAD': unknown revision", exception.Message);
    }

    [Fact]
    public async Task AFailingDiffForAnUnknownBaseThrowsACommandExceptionThatNamesTheDiffAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGit(128, "", "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ListChangedFilesAsync("/tmp/factory-clone-1", "no-such-base"));

        Assert.Contains("diff", exception.Message);
        Assert.Contains("fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.", exception.Message);
    }

    [Fact]
    public async Task ARejectedPushThrowsACommandExceptionThatNamesThePushAndHoldsItsStandardError()
    {
        (Git git, _) = MockedGit(1, "", "! [rejected] factory/run -> factory/run (fetch first)");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("/tmp/factory-clone-1", "factory/run", "test-token"));

        Assert.Contains("push", exception.Message);
        Assert.Contains("! [rejected] factory/run -> factory/run (fetch first)", exception.Message);
    }

    [Fact]
    public async Task APushWithAnInvalidTokenThrowsACommandExceptionThatNamesThePushAndTheFailedAuthentication()
    {
        (Git git, _) = MockedGit(128, "", "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync("/tmp/factory-clone-1", "factory/run", "invalid"));

        Assert.Contains("push", exception.Message);
        Assert.Contains("Authentication failed", exception.Message);
    }

    private static (Git, List<CommandSpec>) MockedGit(int exitCode, string standardOutput, string standardError)
        => MockedRunner(new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput, StandardError = standardError });

    private static (Git, List<CommandSpec>) MockedGitThenFailing(int exitCode, string standardOutput, string standardError)
        => MockedRunner(
            new CommandResult(),
            new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput, StandardError = standardError });

    private static (Git, List<CommandSpec>) MockedRunner(params CommandResult[] results)
    {
        List<CommandSpec> specs = [];
        Queue<CommandResult> resultsInOrder = new(results);
        var mock = new Mock<CommandRunner>();
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(specs.Add)
            .Returns((CommandSpec spec) => Task.FromResult(resultsInOrder.Count is 1 ? resultsInOrder.Peek() : resultsInOrder.Dequeue()));

        return (new Git(mock.Object), specs);
    }
}
