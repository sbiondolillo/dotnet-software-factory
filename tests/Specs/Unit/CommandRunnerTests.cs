using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task RunAsyncThrowsTimeoutExceptionAndStopsTheProgramWhenItOutlivesItsTimeout()
    {
        var runner = new CommandRunner();
        var spec = new CommandSpec
        {
            FileName = "sh",
            Arguments = ["-c", "sleep 5"],
            Timeout = TimeSpan.FromSeconds(1),
        };

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(spec, TestContext.Current.CancellationToken));

        Assert.Contains("sh", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1 second", exception.Message, StringComparison.Ordinal);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(IsProcessRunning(["sh", "-c", "sleep 5"]));
    }

    /// <summary>Whether any process on the machine has exactly <paramref name="argv"/> as its command line,
    /// found by scanning <c>/proc</c> directly since this image has neither <c>pgrep</c> nor <c>ps</c>. Matching
    /// the whole argument vector, not a substring of it, keeps this from matching an unrelated process whose
    /// command line happens to quote this very test's shell command.</summary>
    private static bool IsProcessRunning(string[] argv)
    {
        foreach (var processDirectory in Directory.EnumerateDirectories("/proc"))
        {
            var pidText = Path.GetFileName(processDirectory);
            if (!int.TryParse(pidText, out _))
            {
                continue;
            }

            var cmdlinePath = Path.Combine(processDirectory, "cmdline");
            string cmdline;
            try
            {
                cmdline = File.ReadAllText(cmdlinePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var processArgv = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (processArgv.SequenceEqual(argv, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task RunAsyncThrowsWhenTheProgramIsNotOnPath()
    {
        var runner = new CommandRunner();
        var spec = new CommandSpec { FileName = "no-such-factory-program-xyz" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(spec, TestContext.Current.CancellationToken));

        Assert.Contains("no-such-factory-program-xyz", exception.Message, StringComparison.Ordinal);
    }
}
