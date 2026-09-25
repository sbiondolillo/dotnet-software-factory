using System.Diagnostics;
using System.Globalization;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task AProgramThatExitsNonZeroReturnsACommandResultRatherThanThrowing()
    {
        CommandResult result = await new CommandRunner().RunAsync("sh", ["-c", "echo hello; exit 3"]);

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task AProgramThatOutlivesTheTimeoutIsStoppedAndRunAsyncThrowsANamesTheProgramAndTheTimeout()
    {
        string pidFile = Path.Combine(Path.GetTempPath(), $"command-runner-sleeper-{Guid.NewGuid():N}.pid");
        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => new CommandRunner().RunAsync("sh", ["-c", $"sleep 10 & echo $! > {pidFile}; wait"], null, null, TimeSpan.FromSeconds(1)));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 second", exception.Message);
        Assert.True(ProcessHasExited(int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task AProgramOnNoPathMakesRunAsyncThrowAnExceptionThatNamesTheProgram()
    {
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CommandRunner().RunAsync("factory-nonexistent-program-28da2742"));

        Assert.Contains("factory-nonexistent-program-28da2742", exception.Message);
    }

    private static bool ProcessHasExited(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit(0);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
