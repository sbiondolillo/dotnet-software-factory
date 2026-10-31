using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeatureFilesTests
{
    [Fact]
    public void ReadScenarioTitlesThrowsAFeatureFileExceptionNamingTheFileWhenTheParserRejectsItsContent()
    {
        string directory = Directory.CreateTempSubdirectory("feature-files-tests-").FullName;
        try
        {
            string path = Path.Combine(directory, "invalid.feature");
            File.WriteAllText(path, "This is not a feature file.");

            FeatureFileException exception = Assert.Throws<FeatureFileException>(() => FeatureFiles.ReadScenarioTitles(path));

            Assert.Equal(path, exception.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadScenarioTitlesExceptionMessageHoldsThePositionAndNoTextOfTheFile()
    {
        string directory = Directory.CreateTempSubdirectory("feature-files-tests-").FullName;
        try
        {
            string path = Path.Combine(directory, "invalid.feature");
            const string content = "This is not a feature file.";
            File.WriteAllText(path, content);

            FeatureFileException exception = Assert.Throws<FeatureFileException>(() => FeatureFiles.ReadScenarioTitles(path));

            Assert.Contains("(1:1)", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(content, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadOutlineStartsASpanAtTheCommentsDirectlyAboveItsFirstTag()
    {
        const string content = "Feature: G\n\n  # note\n  @a\n\n  @b\n  Scenario: One\n    Given one\n";

        FeatureOutline outline = FeatureFiles.ReadOutline("g.feature", content);

        Assert.Equal([new ScenarioSpan("One", 3, 8)], outline.Scenarios);
    }

    [Fact]
    public void ReadOutlineEndsASpanAtItsLastLineThatIsNotBlankAndNotAComment()
    {
        const string content = "Feature: G\n\n  Scenario Outline: One <n>\n    Given <n>\n\n    Examples:\n      | n |\n      | 1 |\n  # trailing\n\n  Scenario: Two\n    Given two\n";

        FeatureOutline outline = FeatureFiles.ReadOutline("g.feature", content);

        Assert.Equal([new ScenarioSpan("One <n>", 3, 8), new ScenarioSpan("Two", 11, 12)], outline.Scenarios);
    }

    [Fact]
    public void ReadOutlineTrimsTheNameAndReadsARuleAndABackground()
    {
        const string content = "Feature: G\n\n  Background:\n    Given a\n\n  Rule: R\n\n    Example:   One  \n      Given one\n";

        FeatureOutline outline = FeatureFiles.ReadOutline("g.feature", content);

        Assert.True(outline.HasRule);
        Assert.True(outline.HasBackground);
        Assert.Equal("One", Assert.Single(outline.Scenarios).Name);
    }
}
