using System.Globalization;
using System.Text;

namespace Specs;

/// <summary>
/// Asks the kernel which live processes exist, by argv. A hit is a process whose
/// entire command line is exactly [program] plus arguments, split on NUL. A
/// substring scan is wrong here: the process running this session carries the
/// prompt's text on its own command line, and a child's argv (sleep, 5) is not
/// its parent's (sh, -c, sleep 5).
/// </summary>
public static class ProcessProbe
{
    public static bool IsRunning(string program, params string[] arguments)
    {
        var expected = new List<string> { program };
        expected.AddRange(arguments);

        foreach (var entry in Directory.GetFileSystemEntries("/proc"))
        {
            var pid = entry[(entry.LastIndexOf('/') + 1)..];
            if (!long.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            string? raw;
            try
            {
                raw = File.ReadAllText(Path.Combine(entry, "cmdline"));
            }
            catch (IOException)
            {
                continue; // The process exited between the listing and the read.
            }

            var actual = raw.Split('\0');
            if (actual.Length > 0 && actual[^1] == "")
            {
                actual = actual[..^1];
            }

            if (actual.SequenceEqual(expected))
            {
                return true;
            }
        }

        return false;
    }
}
