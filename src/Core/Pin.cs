using System.Security.Cryptography;

namespace Core;

/// <summary>The differences between a pin and a directory, as ordered relative paths.</summary>
public sealed record PinComparison(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed);

/// <summary>Fingerprints the feature files and build settings files of a directory.</summary>
public sealed class Pin
{
    private static readonly HashSet<string> SettingsNames = new(StringComparer.Ordinal)
    {
        ".editorconfig",
        ".globalconfig",
        "global.json",
        "nuget.config",
        "NuGet.config",
        "NuGet.Config",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Build.rsp",
        "reqnroll.json",
    };

    private readonly Dictionary<string, string> _hashes;

    /// <summary>The file names that <see cref="Pin"/> reads as build settings files.</summary>
    public static IReadOnlyCollection<string> SettingsFileNames => SettingsNames;


    private Pin(Dictionary<string, string> hashes) => _hashes = hashes;

    /// <summary>The pinned relative path (with '/') mapped to the lowercase hex SHA-256 of its bytes.</summary>
    public IReadOnlyDictionary<string, string> Hashes => _hashes;

    /// <summary>Records the pinned files of a directory.</summary>
    public static Pin Take(string directory) => new(Scan(directory));

    /// <summary>Reports which pinned files of a directory differ from this pin.</summary>
    public PinComparison Compare(string directory)
    {
        var now = Scan(directory);
        var changed = now.Where(pair => _hashes.TryGetValue(pair.Key, out var old) && old != pair.Value).Select(pair => pair.Key);
        var added = now.Keys.Where(path => !_hashes.ContainsKey(path));
        var removed = _hashes.Keys.Where(path => !now.ContainsKey(path));
        return new PinComparison(Order(changed), Order(added), Order(removed));
    }

    private static List<string> Order(IEnumerable<string> paths)
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
        var segments = relative.Split('/');
        var directories = segments[..^1];
        if (directories.Any(segment => segment is "bin" or "obj"))
        {
            return false;
        }

        return directories.Length >= 2 && directories[0] == "specs" && directories[1] == "features"
            || SettingsNames.Contains(segments[^1]);
    }
}
