using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task RunAsyncThrowsTimeoutExceptionNamingTheProgramAndTheSeconds()
    {
        var runner = new CommandRunner();
        var request = new CommandRequest
        {
            Program = "sh",
            Arguments = ["-c", "sleep 5"],
            Timeout = TimeSpan.FromSeconds(1),
        };

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1", exception.Message);
    }

    [Fact]
    public async Task RunAsyncThrowsWhenTheProgramIsNotOnPath()
    {
        var runner = new CommandRunner();
        var request = new CommandRequest { Program = "definitely-not-a-real-program-xyz" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("definitely-not-a-real-program-xyz", exception.Message);
    }

    [Fact]
    public async Task ATimedOutProcessIsNoLongerRunningAfterTheTimeoutFires()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"factory-command-runner-test-{Guid.NewGuid():N}");
        try
        {
            var runner = new CommandRunner();
            var request = new CommandRequest
            {
                Program = "sh",
                Arguments = ["-c", $"echo $$ > {pidFile}; sleep 5"],
                Timeout = TimeSpan.FromSeconds(1),
            };

            await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(request, TestContext.Current.CancellationToken));

            // Give the OS a moment to write the pid file and reap the killed
            // process tree, then confirm the killed process no longer exists.
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

            var pidText = (await File.ReadAllTextAsync(pidFile, TestContext.Current.CancellationToken)).Trim();
            Assert.False(Directory.Exists($"/proc/{pidText}"), $"expected /proc/{pidText} to be gone after the timeout killed it");
        }
        finally
        {
            File.Delete(pidFile);
        }
    }
}
