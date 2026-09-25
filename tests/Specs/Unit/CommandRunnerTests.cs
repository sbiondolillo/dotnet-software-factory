using System.ComponentModel;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task RunAsyncReportsTheTimeoutAndKillsAProgramThatOutlivesIt()
    {
        var runner = new CommandRunner();
        const string fileName = "sh";
        var arguments = new[] { "-c", "sleep 5" };

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync(
                "/tmp",
                fileName,
                arguments,
                timeout: TimeSpan.FromSeconds(1),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(fileName, exception.Message);
        Assert.Contains("1", exception.Message);
        Assert.True(
            WaitUntilGone([fileName, .. arguments], TimeSpan.FromSeconds(2)),
            "the killed process is still running");
    }

    [Fact]
    public async Task RunAsyncNamesTheProgramWhenNoPathEntryMatchesIt()
    {
        var runner = new CommandRunner();
        const string fileName = "this-program-does-not-exist-12345";

        var exception = await Assert.ThrowsAsync<Win32Exception>(
            () => runner.RunAsync(
                "/tmp",
                fileName,
                Array.Empty<string>(),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(fileName, exception.Message);
    }

    private static bool WaitUntilGone(IReadOnlyList<string> argv, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsRunning(argv) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        return !IsRunning(argv);
    }

    private static bool IsRunning(IReadOnlyList<string> argv)
    {
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var pid = entry.TrimEnd('/');
            if (!long.TryParse(pid, out _))
            {
                continue;
            }

            string[] processArgv;
            try
            {
                processArgv = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException)
            {
                continue; // The process exited between the listing and the read.
            }

            // An exact argv match: a substring scan would match the test host
            // itself, whose command line carries this session's prompt text.
            if (processArgv.SequenceEqual(argv))
            {
                return true;
            }
        }

        return false;
    }
}
