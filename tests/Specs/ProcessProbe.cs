namespace Specs;

/// <summary>
/// Scans /proc for a process whose full argv equals the given argv, entry for
/// entry. The match is on the NUL-split cmdline, not a substring: this
/// session's own process carries the prompt text on its command line, so a
/// substring scan would report a program running that never was.
/// </summary>
internal static class ProcessProbe
{
    public static bool IsRunning(IReadOnlyList<string> argv)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out _))
            {
                continue;
            }

            string[] commandLine;
            try
            {
                commandLine = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException)
            {
                continue; // The process exited or is unreadable.
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (commandLine.SequenceEqual(argv))
            {
                return true;
            }
        }

        return false;
    }
}
