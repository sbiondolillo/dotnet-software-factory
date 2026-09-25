using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The sad paths <see cref="CommandRunner"/> must take, called straight against the library.</summary>
public class CommandRunnerTests
{
    [Fact]
    public async Task ATimedOutChildIsKilledNotJustReported()
    {
        // "sleep 7" keeps this child's argv unique: the feature's own timeout scenario runs
        // "sleep 5", and xunit may run the two in parallel, so an exact-argv check on "sleep 5"
        // could see the other scenario's child.
        var request = new CommandRequest("sh", ["-c", "sleep 7"], Timeout: TimeSpan.FromSeconds(1));
        var run = new CommandRunner().RunAsync(request);

        // RunAsync runs synchronously until its first await, which comes after Process.Start,
        // so the child is already running here: a process carries its exact argv in /proc.
        Assert.True(
            HasProcessWithExactArgv(["sleep", "7"]) || HasProcessWithExactArgv(["sh", "-c", "sleep 7"]),
            "the timed-out child never ran");

        await Assert.ThrowsAsync<TimeoutException>(() => run);

        // And it is really gone, not just reported: no /proc/<pid>/cmdline carries that
        // exact argv any more.
        var isGone = false;
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !isGone)
        {
            isGone = !HasProcessWithExactArgv(["sleep", "7"]) && !HasProcessWithExactArgv(["sh", "-c", "sleep 7"]);
            if (!isGone)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        Assert.True(isGone, "a process with the timed-out child's argv is still running");
    }

    [Fact]
    public async Task AProgramOnNoPathMakesTheRunnerThrowNamingTheProgram()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => new CommandRunner().RunAsync(new CommandRequest("no-such-program-xyz", [])));

        Assert.Contains("no-such-program-xyz", exception.Message);
    }

    /// <summary>
    /// True when some process's argv, split from /proc/<pid>/cmdline on NUL, equals <paramref name="expected"/>
    /// exactly. A substring scan would false-match the process that carries this test's own arguments.
    /// </summary>
    private static bool HasProcessWithExactArgv(string[] expected)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(directory);
            if (name is null || !int.TryParse(name, out _))
            {
                continue;
            }

            try
            {
                var argv = File.ReadAllText(Path.Combine(directory, "cmdline"))
                    .Split('\0')
                    .Where(argument => argument.Length > 0)
                    .ToArray();
                if (argv.SequenceEqual(expected))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // The process exited while it was being read.
            }
            catch (UnauthorizedAccessException)
            {
                // Another user's process; it cannot be the child.
            }
        }

        return false;
    }
}
