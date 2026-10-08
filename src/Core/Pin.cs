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
    private readonly bool _wide;

    /// <summary>The file names that <see cref="Pin"/> reads as build settings files.</summary>
    public static IReadOnlyCollection<string> SettingsFileNames => SettingsNames;


    private Pin(Dictionary<string, string> hashes, bool wide)
    {
        _hashes = hashes;
        _wide = wide;
    }

    /// <summary>The pinned relative path (with '/') mapped to the lowercase hex SHA-256 of its bytes.</summary>
    public IReadOnlyDictionary<string, string> Hashes => _hashes;

    /// <summary>Records the pinned files of a directory.</summary>
    public static Pin Take(string directory) => new(Scan(directory, wide: false), wide: false);

    /// <summary>Records the files of a refactor run: every file under specs/features/, tests/Specs/ and tests/Integration/, and the settings files.</summary>
    public static Pin TakeRefactor(string directory) => new(Scan(directory, wide: true), wide: true);

    /// <summary>Reports which pinned files of a directory differ from this pin.</summary>
    public PinComparison Compare(string directory)
    {
        Dictionary<string, string> now = Scan(directory, _wide);
        IEnumerable<string> changed = now.Where(pair => _hashes.TryGetValue(pair.Key, out string? old) && old != pair.Value).Select(pair => pair.Key);
        IEnumerable<string> added = now.Keys.Where(path => !_hashes.ContainsKey(path));
        IEnumerable<string> removed = _hashes.Keys.Where(path => !now.ContainsKey(path));
        return new PinComparison(Order(changed), Order(added), Order(removed));
    }

    private static List<string> Order(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static Dictionary<string, string> Scan(string directory, bool wide)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (IsPinned(relative, wide))
            {
                result[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            }
        }

        return result;
    }

    private static bool IsPinned(string relative, bool wide)
    {
        string[] segments = relative.Split('/');
        string[] directories = segments[..^1];
        if (directories.Any(segment => segment is "bin" or "obj"))
        {
            return false;
        }

        if (wide && (relative.StartsWith("specs/features/", StringComparison.Ordinal)
            || relative.StartsWith("tests/Specs/", StringComparison.Ordinal)
            || relative.StartsWith("tests/Integration/", StringComparison.Ordinal)))
        {
            return true;
        }

        return (directories.Length >= 2 && directories[0] == "specs" && directories[1] == "features")
            || SettingsNames.Contains(segments[^1]);
    }
}
