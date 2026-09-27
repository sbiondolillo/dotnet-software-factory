using Gherkin.Ast;

namespace Core;

/// <summary>The scenario titles of one <c>.feature</c> file, in the order the file states them.</summary>
public sealed record FeatureFileScenarios(string Path, IReadOnlyList<string> Titles);

/// <summary>Reads the scenario titles of a <c>.feature</c> file or of every <c>.feature</c> file under a
/// directory, and maps a scenario title to the test method name Reqnroll generates for it.</summary>
public static class FeatureFiles
{
    /// <summary>Reads the scenario titles of the <c>.feature</c> file at <paramref name="path"/>, or of every
    /// <c>.feature</c> file at any depth under it when <paramref name="path"/> names a directory. A file whose
    /// name does not end <c>.feature</c> is never opened.</summary>
    public static IReadOnlyList<FeatureFileScenarios> ReadScenarioTitles(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.feature", SearchOption.AllDirectories)
                .OrderBy(featureFile => featureFile, StringComparer.Ordinal)
                .Select(ReadFile)
                .ToList();
        }

        return [ReadFile(path)];
    }

    /// <summary>The test method name Reqnroll generates for a scenario titled <paramref name="scenarioTitle"/>,
    /// computed by Reqnroll's own naming rule so it never drifts from what Reqnroll itself generates.</summary>
    public static string ToTestName(string scenarioTitle)
        => Reqnroll.Tracing.CodeFormattingExtensions.ToIdentifier(scenarioTitle);

    private static FeatureFileScenarios ReadFile(string path)
    {
        string text = File.ReadAllText(path);
        GherkinDocument document;
        try
        {
            document = new Gherkin.Parser().Parse(new StringReader(text));
        }
        catch (Exception parseError)
        {
            throw new FeatureFileException(path, parseError);
        }

        List<string> titles = [];
        if (document.Feature is not null)
        {
            CollectTitles(document.Feature.Children, titles);
        }

        return new FeatureFileScenarios(path, titles);
    }

    private static void CollectTitles(IEnumerable<IHasLocation> children, List<string> titles)
    {
        foreach (IHasLocation child in children)
        {
            switch (child)
            {
                case Scenario scenario:
                    titles.Add(scenario.Name);
                    break;
                case Rule rule:
                    CollectTitles(rule.Children, titles);
                    break;
            }
        }
    }
}
