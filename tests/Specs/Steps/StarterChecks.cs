using System.Xml.Linq;

namespace Specs.Steps;

/// <summary>The checks the starter template's scenarios make on a copy that <c>dotnet new</c> wrote. Each gives the problems it found, named by path, and an empty list when the copy is right.</summary>
public static class StarterChecks
{
    /// <summary>Compares the files of <paramref name="starter"/>, outside its <c>.template.config</c> directory, with the files of <paramref name="copy"/>.</summary>
    public static IReadOnlyList<string> CompareTrees(string starter, string copy)
    {
        Dictionary<string, string> expected = Files(starter, skipTemplateConfig: true);
        Dictionary<string, string> actual = Files(copy, skipTemplateConfig: false);
        var problems = new List<string>();
        foreach ((string path, string file) in expected)
        {
            if (!actual.TryGetValue(path, out string? other))
            {
                problems.Add($"missing file {path}");
            }
            else if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(other)))
            {
                problems.Add($"differing file {path}");
            }
        }

        problems.AddRange(actual.Keys.Where(path => !expected.ContainsKey(path)).Select(path => $"extra file {path}"));
        return problems;
    }

    /// <summary>The packages a project file references, in order.</summary>
    public static IReadOnlyList<string> PackageReferences(string projectFile) =>
        XDocument.Load(projectFile).Descendants("PackageReference").Select(e => (string?)e.Attribute("Include") ?? "").Order(StringComparer.Ordinal).ToList();

    /// <summary>The problems with the packages a project file references, against <paramref name="expected"/>.</summary>
    public static IReadOnlyList<string> PackageProblems(string projectFile, params string[] expected)
    {
        IReadOnlyList<string> actual = PackageReferences(projectFile);
        var problems = new List<string>();
        problems.AddRange(expected.Except(actual).Select(name => $"{projectFile} lacks the package {name}"));
        problems.AddRange(actual.Except(expected).Select(name => $"{projectFile} references the package {name}"));
        return problems;
    }

    /// <summary>The problems with the versions <c>Directory.Packages.props</c> gives: one entry of <paramref name="expected"/> is a package name and its version.</summary>
    public static IReadOnlyList<string> VersionProblems(string propsFile, IReadOnlyDictionary<string, string> expected)
    {
        Dictionary<string, string> actual = XDocument.Load(propsFile).Descendants("PackageVersion")
            .ToDictionary(e => (string?)e.Attribute("Include") ?? "", e => (string?)e.Attribute("Version") ?? "", StringComparer.Ordinal);
        var problems = new List<string>();
        foreach ((string name, string version) in expected)
        {
            if (!actual.TryGetValue(name, out string? found))
            {
                problems.Add($"{propsFile} gives no version to {name}");
            }
            else if (found != version)
            {
                problems.Add($"{propsFile} gives {name} the version {found}, not {version}");
            }
        }

        return problems;
    }

    /// <summary>The problems with the directory <paramref name="directory"/>, which must hold exactly the files named by <paramref name="names"/>.</summary>
    public static IReadOnlyList<string> ExactFiles(string directory, params string[] names)
    {
        string[] actual = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>().ToArray();
        var problems = new List<string>();
        problems.AddRange(names.Except(actual).Select(name => $"{directory} lacks {name}"));
        problems.AddRange(actual.Except(names).Select(name => $"{directory} holds {name}"));
        return problems;
    }

    /// <summary>The paths of the projects <c>Solution.slnx</c> names, relative to the copy.</summary>
    public static IReadOnlyList<string> SolutionProjects(string copy) =>
        XDocument.Load(Path.Combine(copy, "Solution.slnx")).Descendants("Project").Select(e => (string?)e.Attribute("Path") ?? "").ToList();

    /// <summary>The projects of the copy that have no <c>packages.lock.json</c> beside the project file.</summary>
    public static IReadOnlyList<string> MissingLockFiles(string copy) =>
        SolutionProjects(copy)
            .Where(project => !File.Exists(Path.Combine(copy, Path.GetDirectoryName(project) ?? "", "packages.lock.json")))
            .Select(project => $"no packages.lock.json beside {project}")
            .ToList();

    /// <summary>The directory above the test binaries that holds <c>Solution.slnx</c>.</summary>
    public static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private static Dictionary<string, string> Files(string root, bool skipTemplateConfig) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (Path: Path.GetRelativePath(root, file).Replace('\\', '/'), File: file))
            .Where(entry => !skipTemplateConfig || !entry.Path.StartsWith(".template.config/", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Path, entry => entry.File, StringComparer.Ordinal);
}
