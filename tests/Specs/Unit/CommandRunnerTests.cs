using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task RunAsyncThrowsTimeoutExceptionWhenTheProgramOutlivesTheTimeout()
    {
        var runner = new CommandRunner();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            "sh",
            ["-c", "sleep 5"],
            null,
            null,
            TimeSpan.FromSeconds(1)));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 seconds", exception.Message);
    }

    [Fact]
    public async Task RunAsyncThrowsWhenTheProgramCannotBeStarted()
    {
        var runner = new CommandRunner();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            "this-program-does-not-exist-12345",
            [],
            null,
            null,
            TimeSpan.FromSeconds(5)));

        Assert.Contains("this-program-does-not-exist-12345", exception.Message);
    }
}
