namespace Core;

/// <summary>The differences between a pin and a directory, as ordered relative paths.</summary>
public sealed record PinComparison(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed);

/// <summary>Fingerprints the feature files and build settings files of a work tree, through its <see cref="FileHashes"/>.</summary>
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

    /// <summary>The pinned relative path (with '/') mapped to its hash from <see cref="FileHashes.Read"/>.</summary>
    public IReadOnlyDictionary<string, string> Hashes => _hashes;

    /// <summary>Records the pinned files of a work tree.</summary>
    public static Pin Take(FileHashes files) => new(Scan(files, wide: false), wide: false);

    /// <summary>Records the files of a refactor run: every file under <see cref="FeatureFiles.RelativePath"/>, tests/Specs/ and tests/Integration/, and the settings files.</summary>
    public static Pin TakeRefactor(FileHashes files) => new(Scan(files, wide: true), wide: true);

    /// <summary>Reports which pinned files of a work tree differ from this pin.</summary>
    public PinComparison Compare(FileHashes files)
    {
        Dictionary<string, string> now = Scan(files, _wide);
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

    private static Dictionary<string, string> Scan(FileHashes files, bool wide)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.Read(relative => IsPinned(relative, wide));
    }

    private static bool IsPinned(string relative, bool wide)
    {
        string[] segments = relative.Split('/');
        string[] directories = segments[..^1];
        if (directories.Any(segment => segment is "bin" or "obj"))
        {
            return false;
        }

        if (wide && (FeatureFiles.IsFeaturePath(relative)
            || relative.StartsWith("tests/Specs/", StringComparison.Ordinal)
            || relative.StartsWith("tests/Integration/", StringComparison.Ordinal)))
        {
            return true;
        }

        return FeatureFiles.IsFeaturePath(relative) || SettingsNames.Contains(segments[^1]);
    }
}
