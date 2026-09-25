using System.Diagnostics;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    private static readonly string[] SleepArguments = { "-c", "sleep 5" };

    [Fact]
    public async Task RunAsyncThrowsTimeoutExceptionAndKillsTheChildWhenItOutlastsItsTimeout()
    {
        var runner = new CommandRunner();
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync("sh", SleepArguments, timeout: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1", exception.Message);

        // Give the OS a moment to reap the killed process, then confirm it is gone.
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        Assert.DoesNotContain(
            Process.GetProcesses(),
            process => TryGetProcessName(process) == "sleep");
    }

    [Fact]
    public async Task RunAsyncPassesAnArgumentContainingARealDoubleQuoteThroughUnchanged()
    {
        var runner = new CommandRunner();
        var value = "a \"b\" $HOME c";

        var result = await runner.RunAsync(
            "sh",
            new[] { "-c", "printf %s \"$1\"", "_", value },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(value, result.StandardOutput);
    }

    [Fact]
    public async Task RunAsyncThrowsAnExceptionNamingTheProgramWhenItCannotBeStarted()
    {
        var runner = new CommandRunner();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("no-such-program-xyz", Array.Empty<string>(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("no-such-program-xyz", exception.Message);
    }

    private static string? TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
