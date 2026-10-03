using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Specs;

/// <summary>The checks the starter template's scenarios make on a copy. Each throws <see cref="InvalidOperationException"/> when it fails.</summary>
public static partial class StarterChecks
{
    private static readonly string[] Left = [".template.config/", "bin/", "obj/", "TestResults/"];

    /// <summary>The directory that holds Solution.slnx, above the test assembly.</summary>
    public static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Solution.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Solution.slnx is not above the test assembly");
    }

    /// <summary>The files under a directory, as relative paths with a forward slash.</summary>
    public static IReadOnlyList<string> Files(string directory)
        => [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>The files of the starter the copy is to hold.</summary>
    public static IReadOnlyList<string> StarterFiles(string starter)
        => [.. Files(starter).Where(file => !Left.Any(prefix => file.StartsWith(prefix, StringComparison.Ordinal) || file.Contains("/" + prefix, StringComparison.Ordinal)))];

    /// <summary>Fails when the list lacks a line that holds both the name and the short name.</summary>
    public static void ListShows(string output, string shortName, string name)
    {
        if (!output.Split('\n').Any(line => line.Contains(name, StringComparison.Ordinal) && ListsShortName(line, shortName)))
        {
            throw new InvalidOperationException($"the list shows no '{shortName}' with the name '{name}'");
        }
    }

    /// <summary>Fails when the list holds the short name.</summary>
    public static void ListLacks(string output, string shortName)
    {
        if (output.Split('\n').Any(line => ListsShortName(line, shortName)))
        {
            throw new InvalidOperationException($"the list shows the short name '{shortName}'");
        }
    }

    private static bool ListsShortName(string line, string shortName)
        => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.Split(',').Contains(shortName));

    /// <summary>Fails when the file is missing or its text differs from the expected one. Trailing line feeds are trimmed on both sides before the compare.</summary>
    public static void TextEquals(string path, string expected)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"the copy lacks {Path.GetFileName(path)}");
        }

        string[] actual = File.ReadAllText(path).TrimEnd('\n').Split('\n');
        string[] want = expected.TrimEnd('\n').Split('\n');
        for (int i = 0; i < Math.Max(actual.Length, want.Length); i++)
        {
            string a = i < actual.Length ? actual[i] : "<end of file>";
            string w = i < want.Length ? want[i] : "<end of text>";
            if (a != w)
            {
                throw new InvalidOperationException($"{Path.GetFileName(path)} differs at line {i + 1}: expected '{w}' but found '{a}'");
            }
        }
    }

    /// <summary>The lines of the first XML comment of a file, trimmed.</summary>
    private static string[] CommentLines(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{path} does not exist");
        }

        string text = File.ReadAllText(path);
        int start = text.IndexOf("<!--", StringComparison.Ordinal);
        int end = start < 0 ? -1 : text.IndexOf("-->", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} holds no comment");
        }

        return [.. text[(start + 4)..end].Split('\n').Select(line => line.Trim())];
    }

    /// <summary>Fails when the first line of the file's first comment is not the line.</summary>
    public static void CommentStarts(string path, string line)
    {
        string first = CommentLines(path)[0];
        if (first != line)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)}: the comment starts with '{first}', not '{line}'");
        }
    }

    /// <summary>Fails when a line of the file's first comment is the line.</summary>
    public static void CommentLacks(string path, string line)
    {
        if (CommentLines(path).Contains(line))
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)}: the comment holds the line '{line}'");
        }
    }

    /// <summary>Fails when a file of the starter is missing from the copy or differs in a byte.</summary>
    public static void EveryFileEqual(string starter, string copy)
    {
        foreach (string file in StarterFiles(starter))
        {
            string path = Path.Combine(copy, file);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"the copy lacks {file}");
            }

            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(starter, file))))
            {
                throw new InvalidOperationException($"{file} differs from the starter's");
            }
        }
    }

    /// <summary>Fails when the copy holds a file the starter lacks.</summary>
    public static void NoOtherFile(string starter, string copy)
    {
        HashSet<string> known = [.. StarterFiles(starter)];
        string? extra = Files(copy).FirstOrDefault(file => !known.Contains(file));
        if (extra is not null)
        {
            throw new InvalidOperationException($"the copy holds the further file {extra}");
        }
    }

    /// <summary>Fails when the copy holds a file under the directory.</summary>
    public static void NoFileUnder(string copy, string directory)
    {
        string prefix = directory.TrimEnd('/') + "/";
        string? file = Files(copy).FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal));
        if (file is not null)
        {
            throw new InvalidOperationException($"the copy holds {file}");
        }
    }

    /// <summary>Fails unless the project file references exactly these packages.</summary>
    public static void References(string projectFile, params string[] packages)
    {
        string[] found = [.. XDocument.Load(projectFile).Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .Order(StringComparer.Ordinal)];
        string[] want = [.. packages.Order(StringComparer.Ordinal)];
        if (!found.SequenceEqual(want))
        {
            throw new InvalidOperationException($"{projectFile} references [{string.Join(", ", found)}], not [{string.Join(", ", want)}]");
        }
    }

    /// <summary>Fails unless Solution.slnx names exactly these projects.</summary>
    public static void SolutionNames(string solutionFile, IEnumerable<string> projects)
    {
        string[] found = [.. XDocument.Load(solutionFile).Descendants("Project").Select(e => (string?)e.Attribute("Path") ?? "").Order(StringComparer.Ordinal)];
        string[] want = [.. projects.Order(StringComparer.Ordinal)];
        if (!found.SequenceEqual(want))
        {
            throw new InvalidOperationException($"the solution names [{string.Join(", ", found)}], not [{string.Join(", ", want)}]");
        }
    }

    /// <summary>Fails unless the props file gives the package that version.</summary>
    public static void PackageVersion(string propsFile, string package, string version)
    {
        string? found = XDocument.Load(propsFile).Descendants("PackageVersion")
            .Where(e => (string?)e.Attribute("Include") == package)
            .Select(e => (string?)e.Attribute("Version"))
            .FirstOrDefault();
        if (found != version)
        {
            throw new InvalidOperationException($"{package} has the version {found ?? "(none)"}, not {version}");
        }
    }

    /// <summary>Fails unless the directory holds exactly these files.</summary>
    public static void HoldsOnly(string directory, IEnumerable<string> files)
    {
        string[] want = [.. files.Order(StringComparer.Ordinal)];
        IReadOnlyList<string> found = Files(directory);
        if (!found.SequenceEqual(want))
        {
            throw new InvalidOperationException($"{directory} holds [{string.Join(", ", found)}], not [{string.Join(", ", want)}]");
        }
    }

    /// <summary>Fails when a project lacks a lock file beside it.</summary>
    public static void LockFilesBeside(string copy, IEnumerable<string> projects)
    {
        foreach (string project in projects)
        {
            string lockFile = Path.Combine(copy, Path.GetDirectoryName(project) ?? "", "packages.lock.json");
            if (!File.Exists(lockFile))
            {
                throw new InvalidOperationException($"{project} has no packages.lock.json");
            }
        }
    }

    /// <summary>Fails when the file lacks one of the lines.</summary>
    public static void HoldsLines(string file, params string[] lines)
    {
        string[] have = File.ReadAllLines(file);
        string? missing = lines.FirstOrDefault(line => !have.Contains(line));
        if (missing is not null)
        {
            throw new InvalidOperationException($"{file} lacks the line {missing}");
        }
    }

    /// <summary>Fails unless the directory holds one feature file with one scenario.</summary>
    public static void OneFeatureWithOneScenario(string directory)
    {
        string[] features = [.. Directory.EnumerateFiles(directory, "*.feature", SearchOption.AllDirectories)];
        if (features.Length is not 1)
        {
            throw new InvalidOperationException($"{directory} holds {features.Length} feature files");
        }

        int scenarios = File.ReadAllLines(features[0]).Count(line => ScenarioLine().IsMatch(line));
        if (scenarios is not 1)
        {
            throw new InvalidOperationException($"the feature holds {scenarios} scenarios");
        }
    }

    /// <summary>Fails unless the directory holds one step class.</summary>
    public static void OneStepClass(string directory)
    {
        int classes = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Count();
        if (classes is not 1)
        {
            throw new InvalidOperationException($"{directory} holds {classes} step classes");
        }
    }

    /// <summary>Fails unless the .cs files of the directory define a root command with the argument name.</summary>
    public static void DefinesRootCommandWithName(string directory)
    {
        string text = string.Concat(Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        if (!text.Contains("new RootCommand(", StringComparison.Ordinal) || !text.Contains("Argument<string>(\"name\")", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{directory} defines no root command with the argument name");
        }
    }

    /// <summary>Fails unless the program parses its arguments with the root command that <c>Create()</c> builds.</summary>
    public static void ParsesWithRootCommand(string programFile)
    {
        string text = File.ReadAllText(programFile);
        if (!text.Contains("GreetCommand.Create()", StringComparison.Ordinal) || !text.Contains(".Parse(args)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{programFile} does not parse with the root command");
        }
    }

    [GeneratedRegex(@"^\s*Scenario( Outline)?:")]
    private static partial Regex ScenarioLine();
}
