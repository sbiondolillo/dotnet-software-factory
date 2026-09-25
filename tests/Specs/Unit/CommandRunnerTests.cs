using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task AProgramThatExitsWithCode3ReturnsACommandResultWithExitCode3AndDoesNotThrow()
    {
        var result = await new CommandRunner().RunAsync("sh", ["-c", "exit 3"]);

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task TextWrittenToStandardErrorLandsInStandardErrorAndNoneOfItInStandardOutput()
    {
        var result = await new CommandRunner().RunAsync("sh", ["-c", "echo oops >&2"]);

        Assert.Equal("oops", result.StandardError);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task AProgramStillRunningWhenTheTimeoutPassesIsStoppedAndRunAsyncThrowsATimeoutExceptionThatNamesTheProgramAndTheTimeout()
    {
        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            new CommandRunner().RunAsync("sh", ["-c", "sleep 30"], timeout: TimeSpan.FromMilliseconds(200)));

        Assert.Contains("sh", failure.Message);
        Assert.Contains("200 milliseconds", failure.Message);
    }

    [Fact]
    public async Task AProgramThatIsOnNoPathMakesRunAsyncThrowAnExceptionWhoseMessageNamesTheProgram()
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            new CommandRunner().RunAsync("no-such-command-cd66d78d", []));

        Assert.Contains("no-such-command-cd66d78d", failure.Message);
    }
}
