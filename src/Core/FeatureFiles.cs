using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using Reqnroll.Tracing;

namespace Core;

/// <summary>One scenario of a feature file's text: its name, and the first and the last 1-based line of its span.
/// The span starts at the comment lines directly above the first tag line, or above the keyword line when there is
/// no tag, and ends at the last line that is not blank and not a comment, before the next span.</summary>
public sealed record ScenarioSpan(string Name, int FirstLine, int LastLine);

/// <summary>The parts of a feature file's text that decide how its scenarios change: a <c>Feature:</c> line, a
/// <c>Rule:</c>, a <c>Background:</c>, the description of the feature, and the span of each scenario in file order.</summary>
public sealed record FeatureOutline(bool HasFeature, bool HasRule, bool HasBackground, bool HasDescription, IReadOnlyList<ScenarioSpan> Scenarios);

/// <summary>The feature files of one work tree. Every path in and out is relative to the work tree, as git names it, and starts
/// with <see cref="RelativePath"/>. Every read and write refuses a path that holds a symbolic link, and a parse error of a file on
/// disk holds the parser's positions and no text of the file, since a link or a swap can put a file of the runner there. The static
/// members parse text that a caller already holds, with Gherkin's own parser, and name a test with Reqnroll's own identifier
/// logic, so both answers stay in lock-step with what Reqnroll.xunit.v3 compiles.</summary>
public sealed partial class FeatureFiles(string workTree)
{
    /// <summary>The directory of the feature files, relative to the work tree.</summary>
    public const string RelativePath = "specs/features/";

    private const string Extension = ".feature";

    [GeneratedRegex(@"^\(\d+:\d+\)", RegexOptions.Multiline)]
    private static partial Regex Position();

    /// <summary>Tells whether <paramref name="path"/>, relative to a work tree, names a file under <see cref="RelativePath"/>,
    /// with no empty, <c>.</c> or <c>..</c> segment.</summary>
    public static bool IsFeaturePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.StartsWith(RelativePath, StringComparison.Ordinal)
            && path.Split('/').All(segment => segment is not ("" or "." or ".."));
    }

    /// <summary>The path of each file named <c>*.feature</c> under <see cref="RelativePath"/>, at any depth, in ordinal order. No
    /// file is read. An absent directory gives an empty list.</summary>
    public IReadOnlyList<string> Paths()
    {
        string directory = Path.Combine(workTree, RelativePath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(directory, "*" + Extension, SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(workTree, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Reads the text of the file at <paramref name="path"/>, or null when no file is there. Throws
    /// <see cref="ArgumentException"/> for a path outside <see cref="RelativePath"/>, and <see cref="FeatureFileException"/>
    /// when the path holds a symbolic link.</summary>
    public string? ReadText(string path)
    {
        string full = Full(path);
        try
        {
            return File.ReadAllText(full);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Reads the scenario titles of the file at <paramref name="path"/>, in file order, or an empty list when no file is
    /// there. A <c>Background</c> gives no title. A <c>Scenario Outline</c> gives its one title, whatever its example rows. A
    /// scenario under a <c>Rule</c> is a scenario of the file. Throws as <see cref="ReadText"/> does, and
    /// <see cref="FeatureFileException"/> when the parser rejects the text.</summary>
    public IReadOnlyList<string> ScenarioTitles(string path) => ParseFile(path, ReadScenarioTitles, []);

    /// <summary>Reads each scenario of the file at <paramref name="path"/> as <see cref="ReadScenarioSteps"/> does, or an empty
    /// list when no file is there. Throws as <see cref="ScenarioTitles"/> does.</summary>
    public IReadOnlyList<(string Title, string Steps)> ScenarioSteps(string path) => ParseFile(path, ReadScenarioSteps, []);

    /// <summary>Reads the doc string lines of the file at <paramref name="path"/> as <see cref="ReadOverIndentedDocStringLines"/>
    /// does, or an empty list when no file is there. Throws as <see cref="ScenarioTitles"/> does.</summary>
    public IReadOnlyList<int> OverIndentedDocStringLines(string path) => ParseFile(path, ReadOverIndentedDocStringLines, []);

    /// <summary>Writes <paramref name="text"/> to the file at <paramref name="path"/>, and creates its directory. Throws as
    /// <see cref="ReadText"/> does.</summary>
    public void Write(string path, string text)
    {
        string full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>Removes the file at <paramref name="path"/>. Throws as <see cref="ReadText"/> does.</summary>
    public void Remove(string path) => File.Delete(Full(path));

    /// <summary>Gives the full path of <paramref name="path"/> after the checks of <see cref="ReadText"/>.</summary>
    private string Full(string path)
    {
        if (!IsFeaturePath(path))
        {
            throw new ArgumentException($"The path {path} is not under {RelativePath}.", nameof(path));
        }

        string full = workTree;
        foreach (string segment in path.Split('/'))
        {
            full = Path.Combine(full, segment);
            FileSystemInfo entry = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
            if (entry.LinkTarget is not null)
            {
                throw new FeatureFileException(path, $"{Path.GetRelativePath(workTree, full).Replace(Path.DirectorySeparatorChar, '/')} is a symbolic link");
            }
        }

        return full;
    }

    private T ParseFile<T>(string path, Func<string, string, T> parse, T absent)
    {
        if (ReadText(path) is not string content)
        {
            return absent;
        }

        try
        {
            return parse(path, content);
        }
        catch (FeatureFileException exception)
        {
            // A swap after the check of the links can put a file of the runner here, so the error holds no text of the file.
            IEnumerable<string> positions = Position().Matches(exception.Message).Select(match => match.Value);
            throw new FeatureFileException(path, $"the parser rejects the text at {string.Join(", ", positions)}");
        }
    }

    /// <summary>Reads each scenario of <paramref name="content"/>, a feature file's text, as its title and a signature of its steps:
    /// each step's keyword, text and table or doc string. Two scenarios with one title and one signature have the same steps.
    /// Throws <see cref="FeatureFileException"/> when the parser rejects the content.</summary>
    public static IReadOnlyList<(string Title, string Steps)> ReadScenarioSteps(string path, string content)
    {
        GherkinDocument document = Parse(path, content);
        var scenarios = new List<(string Title, string Steps)>();
        if (document.Feature is not null)
        {
            CollectScenarioSteps(document.Feature.Children, scenarios);
        }

        return scenarios;
    }

    /// <summary>Reads the 1-based line of the opening delimiter of each doc string of <paramref name="content"/> whose
    /// non-blank text all sits deeper than that delimiter. Gherkin strips the delimiter's indent from each text line,
    /// so such text still starts with a space. Throws <see cref="FeatureFileException"/> when the parser rejects the content.</summary>
    public static IReadOnlyList<int> ReadOverIndentedDocStringLines(string path, string content)
    {
        GherkinDocument document = Parse(path, content);
        var lines = new List<int>();
        if (document.Feature is not null)
        {
            CollectDocStrings(document.Feature.Children, lines);
        }

        return lines;
    }

    /// <summary>Reads the outline of <paramref name="content"/>, a feature file's text with LF line ends. A scenario's
    /// name is the parser's name with the spaces trimmed, whatever its keyword. A scenario under a <c>Rule</c> is a
    /// scenario of the file. Throws <see cref="FeatureFileException"/> when the parser rejects the content.</summary>
    public static FeatureOutline ReadOutline(string path, string content)
    {
        GherkinDocument document = Parse(path, content);
        if (document.Feature is null)
        {
            return new FeatureOutline(false, false, false, false, []);
        }

        string[] lines = content.Split('\n');
        HashSet<int> comments = [.. document.Comments.Select(comment => comment.Location.Line)];
        var scenarios = new List<Scenario>();
        var bounds = new List<int>();
        bool hasRule = false;
        bool hasBackground = false;
        foreach (IHasLocation child in document.Feature.Children)
        {
            switch (child)
            {
                case Rule rule:
                    hasRule = true;
                    bounds.Add(rule.Tags.Select(tag => tag.Location.Line).Append(rule.Location.Line).Min());
                    foreach (IHasLocation inner in rule.Children)
                    {
                        if (inner is Scenario scenario)
                        {
                            scenarios.Add(scenario);
                        }
                        else if (inner is Background background)
                        {
                            hasBackground = true;
                            bounds.Add(background.Location.Line);
                        }
                    }

                    break;
                case Background background:
                    hasBackground = true;
                    bounds.Add(background.Location.Line);
                    break;
                case Scenario scenario:
                    scenarios.Add(scenario);
                    break;
                default:
                    break;
            }
        }

        bool IsBlankOrComment(int line) => comments.Contains(line) || lines[line - 1].Trim().Length is 0;

        List<int> firsts = scenarios.ConvertAll(scenario =>
        {
            int first = scenario.Tags.Select(tag => tag.Location.Line).Append(scenario.Location.Line).Min();
            while (first > 1 && comments.Contains(first - 1))
            {
                first--;
            }

            return first;
        });
        bounds.AddRange(firsts);
        var spans = new List<ScenarioSpan>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            int keyword = scenarios[i].Location.Line;
            int last = bounds.Where(bound => bound > keyword).Append(lines.Length + 1).Min() - 1;
            while (last > keyword && IsBlankOrComment(last))
            {
                last--;
            }

            spans.Add(new ScenarioSpan(scenarios[i].Name.Trim(), firsts[i], last));
        }

        return new FeatureOutline(true, hasRule, hasBackground, !string.IsNullOrWhiteSpace(document.Feature.Description), spans);
    }

    /// <summary>Computes the name Reqnroll generates for the test method of a scenario titled
    /// <paramref name="scenarioTitle"/>.</summary>
    public static string ComputeTestName(string scenarioTitle) => CodeFormattingExtensions.ToIdentifier(scenarioTitle);

    private static IReadOnlyList<string> ReadScenarioTitles(string path, string content)
    {
        GherkinDocument document = Parse(path, content);
        var titles = new List<string>();
        if (document.Feature is not null)
        {
            CollectScenarioTitles(document.Feature.Children, titles);
        }

        return titles;
    }

    private static GherkinDocument Parse(string path, string content)
    {
        try
        {
            return new Parser().Parse(new StringReader(content));
        }
        catch (ParserException ex)
        {
            throw new FeatureFileException(path, ex.Message);
        }
    }

    private static void CollectScenarioSteps(IEnumerable<IHasLocation> children, List<(string Title, string Steps)> scenarios)
    {
        foreach (IHasLocation child in children)
        {
            switch (child)
            {
                case Rule rule:
                    CollectScenarioSteps(rule.Children, scenarios);
                    break;
                case Scenario scenario:
                    scenarios.Add((scenario.Name, string.Join('\n', scenario.Steps.Select(StepSignature))));
                    break;
                default:
                    break;
            }
        }
    }

    private static void CollectDocStrings(IEnumerable<IHasLocation> children, List<int> lines)
    {
        foreach (IHasLocation child in children)
        {
            IEnumerable<Step> steps = child switch
            {
                Rule rule => CollectRule(rule, lines),
                Scenario scenario => scenario.Steps,
                Background background => background.Steps,
                _ => [],
            };
            foreach (Step step in steps)
            {
                if (step.Argument is DocString doc)
                {
                    string[] texts = [.. doc.Content.Split('\n')
                        .Select(line => line.TrimEnd('\r'))
                        .Where(line => line.Trim().Length > 0)];
                    if (texts.Length > 0 && texts.All(line => line[0] is ' ' or '\t'))
                    {
                        lines.Add(doc.Location.Line);
                    }
                }
            }
        }
    }

    private static IEnumerable<Step> CollectRule(Rule rule, List<int> lines)
    {
        CollectDocStrings(rule.Children, lines);
        return [];
    }

    private static string StepSignature(Step step) => step.Argument switch
    {
        DataTable table => $"{step.Keyword}{step.Text}\n{string.Join('\n', table.Rows.Select(row => string.Join('|', row.Cells.Select(cell => cell.Value))))}",
        DocString doc => $"{step.Keyword}{step.Text}\n{doc.Content}",
        _ => step.Keyword + step.Text,
    };

    private static void CollectScenarioTitles(IEnumerable<IHasLocation> children, List<string> titles)
    {
        foreach (IHasLocation child in children)
        {
            switch (child)
            {
                case Rule rule:
                    CollectScenarioTitles(rule.Children, titles);
                    break;
                case Scenario scenario:
                    titles.Add(scenario.Name);
                    break;
                default:
                    break;
            }
        }
    }
}
