using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task AChildThatOutlivesTheTimeoutIsStoppedAndReported()
    {
        var runner = new CommandRunner();

        var error = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync("sleep", ["5"], null, null, TimeSpan.FromSeconds(1)));

        // The message names the program and the timeout.
        Assert.Contains("sleep", error.Message);
        Assert.Contains("1", error.Message);

        // The timed-out child is gone: no process with argv "sleep 5" remains.
        Assert.False(ProcessIsRunning("sleep", ["5"]));
    }

    [Fact]
    public async Task AProgramOnNoPathFailsWithANamedError()
    {
        var runner = new CommandRunner();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => runner.RunAsync("this-program-does-not-exist-12345", [], null, null, TimeSpan.FromSeconds(5)));

        Assert.Contains("this-program-does-not-exist-12345", error.Message);
    }

    // Reads each /proc/[0-9]*/cmdline, splits it on NUL, and matches the basename of argv[0]
    // against the program name plus the exact remaining argv. Never a substring scan of the
    // whole cmdline: the process that runs this session carries the prompt text, which
    // contains "sleep 5" and "git", on its own command line. An empty cmdline (a zombie)
    // matches nothing.
    private static bool ProcessIsRunning(string program, IReadOnlyList<string> arguments)
    {
        foreach (var entry in Directory.GetFileSystemEntries("/proc", "[0-9]*"))
        {
            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(entry, "cmdline"));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (cmdline.Length == 0)
            {
                continue;
            }

            var argv = cmdline.Split('\0');
            if (Path.GetFileName(argv[0]) != program)
            {
                continue;
            }

            if (!argv.Skip(1).SequenceEqual(arguments))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
