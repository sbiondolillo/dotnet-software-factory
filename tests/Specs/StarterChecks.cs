using System.Xml.Linq;

namespace Specs;

/// <summary>Reads the starter template and a copy of it, for the checks of the starter template's scenarios.</summary>
internal static class StarterChecks
{
    private static readonly string[] SkippedPrefixes = [".template.config/", "bin/", "obj/", "TestResults/"];

    /// <summary>The <c>templates/starter</c> folder of this repo, found from the test assembly's location.</summary>
    public static string StarterFolder()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "templates", "starter");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("templates/starter was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The relative paths, with <c>/</c>, of every file under <paramref name="root"/> outside <c>.template.config/</c>, <c>bin/</c>, <c>obj/</c> and <c>TestResults/</c>.</summary>
    public static SortedSet<string> Files(string root)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!SkippedPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal) || relative.Contains("/" + prefix, StringComparison.Ordinal)))
            {
                paths.Add(relative);
            }
        }

        return paths;
    }

    /// <summary>One line for each file the copy lacks, each file it adds, and each file whose bytes differ.</summary>
    public static IReadOnlyList<string> Differences(string starter, string copy)
    {
        SortedSet<string> expected = Files(starter);
        SortedSet<string> actual = Files(copy);
        var lines = new List<string>();
        lines.AddRange(expected.Except(actual).Select(path => "missing: " + path));
        lines.AddRange(actual.Except(expected).Select(path => "extra: " + path));
        foreach (string path in expected.Intersect(actual))
        {
            if (!File.ReadAllBytes(Path.Combine(starter, path)).SequenceEqual(File.ReadAllBytes(Path.Combine(copy, path))))
            {
                lines.Add("differs: " + path);
            }
        }

        return lines;
    }

    /// <summary>The packages a project file references.</summary>
    public static IReadOnlyList<string> PackageReferences(string projectFile) =>
        [.. XDocument.Load(projectFile).Descendants("PackageReference").Select(e => (string?)e.Attribute("Include") ?? "").Order(StringComparer.Ordinal)];

    /// <summary>The version each package has in a manifest.</summary>
    public static IReadOnlyDictionary<string, string> PackageVersions(string propsFile) =>
        XDocument.Load(propsFile).Descendants("PackageVersion")
            .ToDictionary(e => (string?)e.Attribute("Include") ?? "", e => (string?)e.Attribute("Version") ?? "", StringComparer.Ordinal);

    /// <summary>The project paths a solution file names.</summary>
    public static IReadOnlyList<string> Projects(string solutionFile) =>
        [.. XDocument.Load(solutionFile).Descendants("Project").Select(e => (string?)e.Attribute("Path") ?? "").Order(StringComparer.Ordinal)];
}
