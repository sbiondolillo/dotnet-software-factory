using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task ListingChangedFilesAgainstAnUnknownBaseRevisionThrowsAnExceptionHoldingGitsStandardError()
    {
        var runner = new Mock<CommandRunner>();
        runner
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("diff"))))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 128,
                StandardError = "fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.",
            });
        var git = new Git(runner.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(() => git.ListChangedFilesAsync("no-such-base"));

        Assert.Contains("fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.", exception.Message);
    }

    [Fact]
    public async Task ARejectedPushThrowsAnExceptionHoldingGitsStandardError()
    {
        var runner = new Mock<CommandRunner>();
        runner
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("push"))))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 1,
                StandardError = "! [rejected] factory/run -> factory/run (fetch first)",
            });
        var git = new Git(runner.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(() => git.PushAsync("factory/run"));

        Assert.Contains("! [rejected] factory/run -> factory/run (fetch first)", exception.Message);
    }

    [Fact]
    public async Task APushWithAnInvalidTokenThrowsAnExceptionNamingTheAuthenticationFailure()
    {
        var runner = new Mock<CommandRunner>();
        runner
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("push"))))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 128,
                StandardError = "fatal: Authentication failed for 'https://github.com/sbiondolillo/factory-scratch.git/'",
            });
        var git = new Git(runner.Object);

        var exception = await Assert.ThrowsAsync<GitCommandException>(() => git.PushAsync("factory/run"));

        Assert.Contains("Authentication failed", exception.Message);
    }
}
