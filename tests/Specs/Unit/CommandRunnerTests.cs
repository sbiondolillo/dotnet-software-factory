using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

/// <summary>
/// The sad paths of <see cref="CommandRunner"/>, tested in process against local programs,
/// plus a test that a caller can mock the virtual <c>RunAsync</c>.
/// </summary>
public sealed class CommandRunnerTests
{
    [Fact]
    public async Task AProgramStillRunningWhenTheTimeoutPassesIsStoppedAndTheExceptionNamesIt()
    {
        var sleepPidsBefore = SleepingPids();
        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => new CommandRunner().RunAsync("sleep", ["30"], timeout: TimeSpan.FromSeconds(1)));

        Assert.Contains("sleep", exception.Message);
        Assert.Contains("1 seconds", exception.Message);

        // No spawned process is left running.
        Assert.Empty(SleepingPids().Except(sleepPidsBefore));
    }

    [Fact]
    public async Task AProgramOnNoPathThrowsNamingTheProgram()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CommandRunner().RunAsync("factory-not-a-real-program", []));

        Assert.Contains("factory-not-a-real-program", exception.Message);
    }

    [Fact]
    public async Task TextWrittenToStandardErrorNeverAppearsInStandardOutputAndViceVersa()
    {
        var result = await new CommandRunner().RunAsync(
            "sh",
            ["-c", "echo the stdout line; echo the stderr line >&2"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("the stdout line", result.StandardOutput);
        Assert.DoesNotContain("the stderr line", result.StandardOutput);
        Assert.Contains("the stderr line", result.StandardError);
        Assert.DoesNotContain("the stdout line", result.StandardError);
    }

    [Fact]
    public async Task AProgramThatWritesOneMegabyteToEachStreamCompletesWithBothOutputsWhole()
    {
        var result = await new CommandRunner().RunAsync(
            "python3",
            ["-c", "import sys; sys.stdout.write('A' * 1048576); sys.stderr.write('E' * 1048576)"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1048576, result.StandardOutput.Length);
        Assert.Equal(new string('A', 1048576), result.StandardOutput);
        Assert.Equal(1048576, result.StandardError.Length);
        Assert.Equal(new string('E', 1048576), result.StandardError);
    }

    [Fact]
    public async Task ACallerCanMockRunAsyncBecauseItIsVirtual()
    {
        var canned = new CommandResult(7, "canned output", "canned error");
        var arguments = (IEnumerable<string>)Array.Empty<string>();
        var runner = new Mock<CommandRunner>();
        runner
            .Setup(mocked => mocked.RunAsync("canned", arguments, null, null, null))
            .ReturnsAsync(canned);

        var result = await runner.Object.RunAsync("canned", arguments, null, null, null);

        Assert.Same(canned, result);
    }

    private static HashSet<int> SleepingPids()
    {
        var pids = new HashSet<int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                if (File.ReadAllText(Path.Combine(directory, "comm")).TrimEnd() == "sleep")
                {
                    pids.Add(pid);
                }
            }
            catch (IOException)
            {
                // The process exited while it was being read.
            }
            catch (UnauthorizedAccessException)
            {
                // The process belongs to another user.
            }
        }

        return pids;
    }
}
