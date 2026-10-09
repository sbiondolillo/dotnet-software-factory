using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Specs;

/// <summary>Finds a program that this test run started by its exact argv, reading /proc.</summary>
internal static class ProcessScan
{
    /// <summary>
    /// The variable that marks each program this test run starts. Every program inherits it from the test process,
    /// and a test run on the same machine has its own value, so its programs never match.
    /// </summary>
    public const string MarkerName = "SPECS_TEST_RUN";

    /// <summary>How long <see cref="AssertGoneAsync"/> waits for a program to go.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(1);

    private static readonly string RunId = Guid.NewGuid().ToString("N");

    [ModuleInitializer]
    internal static void MarkTheTestRun() => Environment.SetEnvironmentVariable(MarkerName, RunId);

    /// <summary>
    /// True iff some program that this test run started has an argv equal to <paramref name="argv"/> exactly, in order.
    /// A substring scan is wrong: the <c>sh -c sleep 10</c> wrapper's cmdline also
    /// contains "sleep 10" but its argv is not <c>["sleep", "10"]</c>.
    /// </summary>
    public static bool HasProcessWithExactArgv(string[] argv) => FindOwn(argv).Count > 0;

    /// <summary>Waits up to <see cref="Deadline"/> for every program of this test run with that argv to go, and fails with each one that is left.</summary>
    public static async Task AssertGoneAsync(string[] argv)
    {
        var clock = Stopwatch.StartNew();
        List<int> left = FindOwn(argv);
        while (left.Count > 0 && clock.Elapsed < Deadline)
        {
            await Task.Delay(50);
            left = FindOwn(argv);
        }

        if (left.Count > 0)
        {
            Assert.Fail($"The program {string.Join(' ', argv)} still runs after {Deadline.TotalSeconds} s: {string.Join(", ", left.Select(Describe))}");
        }
    }

    private static List<int> FindOwn(string[] argv)
    {
        string marker = $"{MarkerName}={RunId}";
        List<int> found = [];
        foreach (string entry in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(entry), out int pid)
                && Read(pid, "cmdline").SequenceEqual(argv)
                && Read(pid, "environ").Contains(marker))
            {
                found.Add(pid);
            }
        }

        return found;
    }

    private static string[] Read(int pid, string file)
    {
        try
        {
            return Encoding.UTF8.GetString(File.ReadAllBytes($"/proc/{pid}/{file}")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Describe(int pid)
    {
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return $"pid {pid} (state {fields[0]}, parent {fields[1]})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"pid {pid} (gone before its state was read)";
        }
    }
}
