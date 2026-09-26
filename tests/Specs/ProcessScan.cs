using System.Text;

namespace Specs;

/// <summary>Finds a process by its exact argv, reading /proc.</summary>
internal static class ProcessScan
{
    /// <summary>
    /// True iff some process's argv equals <paramref name="argv"/> exactly, in order.
    /// A substring scan is wrong: the <c>sh -c sleep 10</c> wrapper's cmdline also
    /// contains "sleep 10" but its argv is not <c>["sleep", "10"]</c>.
    /// </summary>
    public static bool HasProcessWithExactArgv(string[] argv)
    {
        foreach (string entry in Directory.EnumerateDirectories("/proc"))
        {
            string pid = Path.GetFileName(entry);
            if (!pid.All(char.IsDigit))
            {
                continue;
            }

            byte[] cmdline;
            try
            {
                cmdline = File.ReadAllBytes($"/proc/{pid}/cmdline");
            }
            catch (IOException)
            {
                continue;
            }

            string[] arguments = Encoding.UTF8.GetString(cmdline).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (arguments.SequenceEqual(argv))
            {
                return true;
            }
        }

        return false;
    }
}
