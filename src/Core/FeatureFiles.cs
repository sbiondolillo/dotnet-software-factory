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

/// <summary>Answers the two questions the factory's test-report step needs about a feature file:
/// which scenarios it holds, and which test method Reqnroll generates for a scenario's title.
/// Reads with Gherkin's own parser and names with Reqnroll's own identifier logic, so both
/// answers stay in lock-step with what Reqnroll.xunit.v3 actually compiles.</summary>
public static partial class FeatureFiles
{
    [GeneratedRegex(@"^\(\d+:\d+\)", RegexOptions.Multiline)]
    private static partial Regex Position();

    /// <summary>Reads the scenario titles of the feature file at <paramref name="featureFilePath"/>, in the
    /// order they appear. A <c>Background</c> contributes no title. A <c>Scenario Outline</c> contributes
    /// its one title regardless of its example rows. A scenario under a <c>Rule</c> is a scenario of the
    /// file. Throws <see cref="FeatureFileException"/> when the path is a symbolic link, or when the parser rejects the file's content.</summary>
    public static IReadOnlyList<string> ReadScenarioTitles(string featureFilePath)
    {
        // A parser error quotes the text, so a link to a file of the runner would put that file in a message.
        if (new FileInfo(featureFilePath).LinkTarget is not null)
        {
            throw new FeatureFileException(featureFilePath, "the file is a symbolic link");
        }

        string content = File.ReadAllText(featureFilePath);
        try
        {
            return ReadScenarioTitles(featureFilePath, content);
        }
        catch (FeatureFileException exception)
        {
            // A linked directory, or a swap after the check above, can put a file of the runner here, so the error holds no text of the file.
            IEnumerable<string> positions = Position().Matches(exception.Message).Select(match => match.Value);
            throw new FeatureFileException(featureFilePath, $"the parser rejects the text at {string.Join(", ", positions)}");
        }
    }

    /// <summary>Scans <paramref name="directoryPath"/> at any depth for files named <c>*.feature</c>, and
    /// reads the scenario titles of each. No other file is read. Returns each feature file's path relative
    /// to <paramref name="directoryPath"/> paired with its scenario titles, ordered by that relative path.</summary>
    public static IReadOnlyList<(string RelativePath, IReadOnlyList<string> Titles)> ReadScenarioTitlesFromDirectory(string directoryPath)
    {
        var results = new List<(string RelativePath, IReadOnlyList<string> Titles)>();
        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*.feature", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string relativePath = Path.GetRelativePath(directoryPath, filePath).Replace(Path.DirectorySeparatorChar, '/');
            results.Add((relativePath, ReadScenarioTitles(filePath)));
        }

        return results;
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

    private static List<string> ReadScenarioTitles(string path, string content)
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
