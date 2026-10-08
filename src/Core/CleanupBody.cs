using System.Text;
using System.Text.RegularExpressions;

namespace Core;

/// <summary>Reads the scope and the check command of a cleanup issue from its body.</summary>
public static partial class CleanupBody
{
    private const string ScopeHeading = "## Scope";
    private const string CheckHeading = "## Check";

    /// <summary>The scope entries under <c>## Scope</c>, one for each non-blank line; empty when the heading is absent or holds none.</summary>
    public static IReadOnlyList<string> ReadScope(string body)
    {
        List<string> entries = [];
        foreach (string line in Section(body, ScopeHeading) ?? [])
        {
            string trimmed = line.Trim();
            if (trimmed.Length is 0 || OpensFence(trimmed) is not null)
            {
                continue;
            }

            Match bulleted = BulletedPath().Match(trimmed);
            entries.Add(!bulleted.Success ? trimmed : bulleted.Groups[1].Success ? bulleted.Groups[1].Value : bulleted.Groups[2].Value);
        }

        return entries;
    }

    /// <summary>The text of the first fenced block under <c>## Check</c>, or null when there is none or it is blank or unclosed.</summary>
    public static string? ReadCheck(string body)
    {
        string? fence = null;
        List<string> command = [];
        foreach (string line in Section(body, CheckHeading) ?? [])
        {
            string trimmed = line.Trim();
            if (fence is null)
            {
                fence = OpensFence(trimmed);
                continue;
            }

            if (ClosesFence(trimmed, fence))
            {
                string text = string.Join('\n', command);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            command.Add(line);
        }

        return null;
    }

    /// <summary>Tells whether <paramref name="path"/> is covered by an entry: a directory ending in /, a glob with *, or exactly that path.</summary>
    public static bool InScope(IReadOnlyList<string> scope, string path) =>
        scope.Any(entry =>
            entry.EndsWith('/') ? path.StartsWith(entry, StringComparison.Ordinal)
            : entry.Contains('*', StringComparison.Ordinal) ? Glob(entry).IsMatch(path)
            : path == entry);

    private static Regex Glob(string entry)
    {
        var pattern = new StringBuilder("^");
        for (int i = 0; i < entry.Length; i++)
        {
            if (entry[i] is '*')
            {
                if (i + 1 < entry.Length && entry[i + 1] is '*')
                {
                    pattern.Append(".*");
                    i++;
                }
                else
                {
                    pattern.Append("[^/]*");
                }
            }
            else
            {
                pattern.Append(Regex.Escape(entry[i].ToString()));
            }
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    private static string? OpensFence(string trimmed)
    {
        foreach (char mark in new[] { '`', '~' })
        {
            int count = trimmed.TakeWhile(c => c == mark).Count();
            if (count >= 3 && (mark is '~' || !trimmed[count..].Contains('`', StringComparison.Ordinal)))
            {
                return new string(mark, count);
            }
        }

        return null;
    }

    private static bool ClosesFence(string trimmed, string fence) =>
        trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.All(c => c == fence[0]);

    private static List<string>? Section(string body, string heading)
    {
        string? fence = null;
        bool inside = false;
        bool found = false;
        List<string> lines = [];
        foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();
            if (fence is null && Heading().IsMatch(trimmed))
            {
                if (inside)
                {
                    break;
                }

                inside = trimmed == heading;
                found |= inside;
                continue;
            }

            fence = fence is null ? OpensFence(trimmed) : ClosesFence(trimmed, fence) ? null : fence;
            if (inside)
            {
                lines.Add(line);
            }
        }

        return found ? lines : null;
    }

    [GeneratedRegex(@"^#{1,6}\s")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?:[-*+]\s+)?`([^`]+)`$|^[-*+]\s+(\S.*)$")]
    private static partial Regex BulletedPath();
}
