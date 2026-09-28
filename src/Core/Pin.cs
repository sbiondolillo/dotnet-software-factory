using System.Security.Cryptography;

namespace Core;

/// <summary>The paths that differ between a pin and a directory.</summary>
public sealed record PinComparison(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed);

/// <summary>The SHA-256 of each pinned file in a directory.</summary>
public sealed class Pin
{
    private static readonly HashSet<string> SettingsNames = new(StringComparer.Ordinal)
    {
        ".editorconfig",
        "global.json",
        "nuget.config",
        "Directory.Build.props",
        "Directory.Build.targets",
        "reqnroll.json",
    };

    private Pin(IReadOnlyDictionary<string, string> hashes) => Hashes = hashes;

    /// <summary>Hash by relative path.</summary>
    public IReadOnlyDictionary<string, string> Hashes { get; }

    public static Pin Take(string directory) => new(Scan(directory));

    public PinComparison Compare(string directory)
    {
        var now = Scan(directory);
        var changed = Hashes.Where(p => now.TryGetValue(p.Key, out var h) && h != p.Value).Select(p => p.Key);
        var added = now.Keys.Where(k => !Hashes.ContainsKey(k));
        var removed = Hashes.Keys.Where(k => !now.ContainsKey(k));
        return new PinComparison(Sorted(changed), Sorted(added), Sorted(removed));
    }

    private static List<string> Sorted(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static Dictionary<string, string> Scan(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (IsPinned(relative))
            {
                result[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            }
        }

        return result;
    }

    private static bool IsPinned(string relative)
    {
        var parts = relative.Split('/');
        if (parts[..^1].Any(p => p is "bin" or "obj"))
        {
            return false;
        }

        return SettingsNames.Contains(parts[^1])
            || (parts.Length > 2 && parts[0] is "specs" && parts[1] is "features");
    }
}
