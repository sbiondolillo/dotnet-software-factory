using Core;
using Xunit;

namespace Specs.Unit;

public class CommandRunnerTests
{
    [Fact]
    public async Task AProgramThatExitsWithCodeThreeIsReturnedAsDataNotThrown()
    {
        string[] arguments = { "-c", "exit 3" };
        CommandResult result = await new CommandRunner().RunAsync("sh", arguments, null, null, TimeSpan.FromSeconds(60));

        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task AProgramStillRunningWhenTheTimeoutPassesThrowsATimeoutExceptionThatNamesTheProgramAndTheTimeoutAndIsStopped()
    {
        string[] arguments = { "-c", "sleep 299" };
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            new CommandRunner().RunAsync("sh", arguments, null, null, TimeSpan.FromSeconds(1)));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 seconds", exception.Message);
        await WaitUntilStopped("sleep 299");
    }

    [Fact]
    public async Task AProgramThatIsOnNoPathThrowsAnExceptionThatNamesTheProgram()
    {
        string[] arguments = { "--version" };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CommandRunner().RunAsync("no-such-program-for-unit-tests", arguments, null, null, TimeSpan.FromSeconds(60)));

        Assert.Contains("no-such-program-for-unit-tests", exception.Message);
    }

    [Fact]
    public async Task TextWrittenToStandardErrorNeverLeaksIntoStandardOutput()
    {
        string[] arguments = { "-c", "echo error text >&2" };
        CommandResult result = await new CommandRunner().RunAsync("sh", arguments, null, null, TimeSpan.FromSeconds(60));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("error text", result.StandardError);
        Assert.DoesNotContain("error text", result.StandardOutput);
    }

    [Fact]
    public async Task OneMegabyteWrittenToEachStreamArrivesWholeWithoutDeadlocking()
    {
        const string script = "head -c 1048576 /dev/zero | tr '\\0' y; head -c 1048576 /dev/zero | tr '\\0' e >&2";
        string[] arguments = { "-c", script };
        CommandResult result = await new CommandRunner().RunAsync("sh", arguments, null, null, TimeSpan.FromSeconds(60));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1048576, result.StandardOutput.Length);
        Assert.All(result.StandardOutput, c => Assert.Equal('y', c));
        Assert.Equal(1048576, result.StandardError.Length);
        Assert.All(result.StandardError, c => Assert.Equal('e', c));
    }

    // The image has no ps or pgrep, so "stopped" is checked by scanning every
    // /proc/<pid>/cmdline for the marker. sleep 299 is unique to this test: the
    // feature scenario's sleep 60 can never match it under parallel execution.
    private static async Task WaitUntilStopped(string marker)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (MarkerRunning(marker) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.False(MarkerRunning(marker));
    }

    private static bool MarkerRunning(string marker)
    {
        foreach (string entry in Directory.EnumerateDirectories("/proc"))
        {
            string pid = Path.GetFileName(entry);
            if (!pid.All(char.IsDigit))
            {
                continue;
            }

            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(entry, "cmdline"));
            }
            catch (IOException)
            {
                // The process exited between the listing and the read.
                continue;
            }

            // An exact argv entry or exact argv, not a substring: a process that merely
            // mentions the marker in a longer argument does not count as running it.
            string[] arguments = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (arguments.Any(argument => argument == marker) || string.Join(" ", arguments) == marker)
            {
                return true;
            }
        }

        return false;
    }
}
