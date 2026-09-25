using System.Globalization;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task AProgramStillRunningWhenTheTimeoutPassesIsKilledAsATreeAndRunAsyncThrowsANamingIt()
    {
        var request = new CommandRequest("sh", ["-c", "sleep 29"], Timeout: TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => CommandRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 second", exception.Message);
        await AssertNoProcessMatchingAsync("sleep 29");
    }

    [Theory]
    [InlineData("factory-no-such-program-9f3d")]
    [InlineData("factory-no-such-program-1a2b")]
    public async Task AProgramOnNoPathMakesRunAsyncThrowAnExceptionThatNamesTheProgram(string program)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CommandRunner.RunAsync(new CommandRequest(program, []), TestContext.Current.CancellationToken));

        Assert.Contains(program, exception.Message);
    }

    private static async Task AssertNoProcessMatchingAsync(string pattern)
    {
        for (var attempt = 0; attempt < 10 && ProcessMatchingRuns(pattern); attempt++)
        {
            await Task.Delay(250);
        }

        Assert.False(ProcessMatchingRuns(pattern), $"a process matching '{pattern}' is still running");
    }

    private static bool ProcessMatchingRuns(string pattern)
    {
        var excluded = SelfAndAncestors();
        foreach (var directory in Directory.EnumerateDirectories("/proc", "*"))
        {
            var name = Path.GetFileName(directory);
            if (!name.All(char.IsDigit) || excluded.Contains(name))
            {
                continue;
            }

            string commandLine;
            try
            {
                commandLine = File.ReadAllText(Path.Combine(directory, "cmdline"));
            }
            catch (IOException)
            {
                continue; // The process exited between the listing and the read.
            }

            if (commandLine.Replace('\0', ' ').Contains(pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> SelfAndAncestors()
    {
        var pids = new HashSet<string> { Environment.ProcessId.ToString(CultureInfo.InvariantCulture) };
        for (var pid = Environment.ProcessId; (pid = ParentProcessId(pid)) > 1;)
        {
            pids.Add(pid.ToString(CultureInfo.InvariantCulture));
        }

        return pids;
    }

    private static int ParentProcessId(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return int.Parse(fields[1], CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
