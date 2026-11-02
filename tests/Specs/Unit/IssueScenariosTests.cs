using Core;
using Xunit;

namespace Specs.Unit;

public sealed class IssueScenariosTests : IDisposable
{
    private const string Path0 = "specs/features/greeting.feature";

    private const string One = "  Scenario: One\n    Given one\n";
    private const string Two = "  Scenario: Two\n    Given two\n";
    private const string Three = "  Scenario: Three\n    Given three\n";

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("issue-scenarios-tests-");

    public void Dispose() => _work.Delete(recursive: true);

    private void Main(string text, string path = Path0)
    {
        string full = Path.Combine(_work.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private string Read(string path = Path0) => File.ReadAllText(Path.Combine(_work.FullName, path));

    private IssueScenariosResult Apply(string body) => IssueScenarios.Apply(body, _work.FullName);

    private static string Block(string text, string fence = "```") => $"## Scenarios\n\n{fence}gherkin\n# {Path0}\n{text}{fence}\n";

    [Fact]
    public void HoldsIsFalseForAHeadingInsideAFencedBlock() =>
        Assert.False(IssueScenarios.Holds("## Notes\n\n```\n## Scenarios\n```\n"));

    [Fact]
    public void HoldsIsTrueForRemovedScenariosAlone() =>
        Assert.True(IssueScenarios.Holds("## Removed scenarios\n\n- `specs/features/a.feature`: One\n"));

    [Fact]
    public void ABlockFencedWithTildesIsRead()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(Block(Two, "~~~"));

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n" + One + "\n" + Two, Read());
    }

    [Fact]
    public void AFenceIndentedFourSpacesDoesNotCloseTheBlock()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(Block("  Scenario: Two\n    Given the text\n    ```\n    words\n    ```\n"));

        Assert.Empty(result.Faults);
        Assert.EndsWith("    ```\n    words\n    ```\n", Read(), StringComparison.Ordinal);
    }

    [Fact]
    public void AShorterFenceDoesNotCloseTheBlock()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(Block("  Scenario: Two\n    Given the text\n      ```\n      words\n      ```\n", "````"));

        Assert.Empty(result.Faults);
        Assert.Contains("      words\n", Read(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACommentDirectlyAboveAScenarioMovesWithItsReplace()
    {
        Main("Feature: G\n\n" + One + "\n  # about two\n" + Two);

        IssueScenariosResult result = Apply(Block("  # new note\n  Scenario: Two\n    Given a new two\n"));

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n" + One + "\n  # new note\n  Scenario: Two\n    Given a new two\n", Read());
    }

    [Fact]
    public void ACommentAfterABlankLineStaysWhereItIs()
    {
        Main("Feature: G\n\n" + One + "\n  # between\n\n" + Two);

        IssueScenariosResult result = Apply("## Removed scenarios\n\n- `" + Path0 + "`: One\n");

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n  # between\n\n" + Two, Read());
    }

    [Fact]
    public void RemovingTheLastScenarioTakesTheBlankLinesBeforeIt()
    {
        Main("Feature: G\n\n" + One + "\n\n" + Two);

        IssueScenariosResult result = Apply("## Removed scenarios\n\n- `" + Path0 + "`: Two\n");

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n" + One, Read());
    }

    [Fact]
    public void RemovingAMiddleScenarioTakesTheBlankLinesAfterIt()
    {
        Main("Feature: G\n\n" + One + "\n" + Two + "\n" + Three);

        IssueScenariosResult result = Apply("## Removed scenarios\n\n* `" + Path0 + "`: Two\n");

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n" + One + "\n" + Three, Read());
    }

    [Fact]
    public void RemovalsAndUpsertsApplyTogetherToTheTextOnMain()
    {
        Main("Feature: G\n\n" + One + "\n" + Two);

        IssueScenariosResult result = Apply(Block("  Scenario: Two\n    Given a new two\n\n" + Three) + "\n## Removed scenarios\n\n- `" + Path0 + "`: One\n");

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n  Scenario: Two\n    Given a new two\n\n" + Three, Read());
    }

    [Fact]
    public void TwoNewScenariosGoAtTheEndInTheOrderOfTheBlock()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(Block(Three + "\n" + Two));

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n" + One + "\n" + Three + "\n" + Two, Read());
    }

    [Fact]
    public void AnExampleKeywordReplacesAScenarioOfTheSameName()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(Block("  Example: One\n    Given a new one\n"));

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n  Example: One\n    Given a new one\n", Read());
    }

    [Fact]
    public void AMainFileWithCrlfLineEndsIsWrittenWithLf()
    {
        Main("Feature: G\r\n\r\n  Scenario: One\r\n    Given one\r\n");

        IssueScenariosResult result = Apply(Block(Two));

        Assert.Empty(result.Faults);
        Assert.Equal("Feature: G\n\n" + One + "\n" + Two, Read());
    }

    [Fact]
    public void AFaultWritesNoFile()
    {
        Main("Feature: G\n\n" + One);
        Main("Feature: H\n\n" + One, "specs/features/other.feature");

        IssueScenariosResult result = Apply(Block(Two) + "\n## Removed scenarios\n\n- `specs/features/other.feature`: Missing\n");

        Assert.Single(result.Faults);
        Assert.Equal("Feature: G\n\n" + One, Read());
        Assert.Empty(result.Written);
    }

    [Fact]
    public void TheResultNamesTheFilesWrittenAndRemoved()
    {
        Main("Feature: G\n\n" + One);
        Main("Feature: H\n\n" + One, "specs/features/other.feature");

        IssueScenariosResult result = Apply(Block(Two) + "\n## Removed scenarios\n\n- `specs/features/other.feature`: One\n");

        Assert.Equal([Path0], result.Written);
        Assert.Equal(["specs/features/other.feature"], result.Removed);
        Assert.False(File.Exists(Path.Combine(_work.FullName, "specs/features/other.feature")));
    }

    [Fact]
    public void ABodyWithoutEitherHeadingIsNotHeld() =>
        Assert.False(IssueScenarios.Holds("## Acceptance criteria\n\n- The total is the sum.\n"));

    private const string NoneBody = "## Scenarios\n\nNone.\nThe fix is to a workflow file, which no test of the suite runs.\n\n## Done when\n\n- [ ] The step reads the key.\n";

    [Fact]
    public void NoneWritesNoFileAndGivesNoFault()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(NoneBody);

        Assert.True(IssueScenarios.Holds(NoneBody));
        Assert.Empty(result.Faults);
        Assert.Empty(result.Written);
        Assert.Empty(result.Removed);
        Assert.Equal("Feature: G\n\n" + One, Read());
        Assert.Single(Directory.EnumerateFiles(_work.FullName, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void NoneWithCrlfLineEndsAndIndentIsRead()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(NoneBody.Replace("None.", "  None.  ", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Empty(result.Faults);
        Assert.Empty(result.Written);
    }

    [Fact]
    public void NoneInARepoWithNoFeatureFileGivesNoFault() => Assert.Empty(Apply(NoneBody).Faults);

    [Fact]
    public void NoneWithRemovedScenariosRemovesThem()
    {
        Main("Feature: G\n\n" + One + "\n" + Two);

        IssueScenariosResult result = Apply(NoneBody + "\n## Removed scenarios\n\n- `" + Path0 + "`: Two\n");

        Assert.Empty(result.Faults);
        Assert.Equal([Path0], result.Written);
        Assert.Equal("Feature: G\n\n" + One, Read());
    }

    [Theory]
    [InlineData("## Scenarios\n\nNone. The fix is to a workflow file.\n")]
    [InlineData("## Scenarios\n\nThe fix is to a workflow file.\nNone.\n")]
    [InlineData("## Scenarios\n\nnone.\n")]
    public void TextOtherThanALineNoneFirstIsNoFencedBlock(string body)
    {
        Main("Feature: G\n\n" + One);

        Assert.Equal(
            ["The heading ## Scenarios holds no fenced block. Put the scenarios of each feature file in a fenced block under it."],
            Apply(body).Faults);
    }

    [Fact]
    public void NoneInsideAFencedBlockOfAnotherHeadingIsNotNone()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply("## Notes\n\n```\nNone.\n```\n\n## Scenarios\n\nThe scenarios follow.\n");

        Assert.Contains("The heading ## Scenarios holds no fenced block. Put the scenarios of each feature file in a fenced block under it.", result.Faults);
    }

    [Fact]
    public void NoneBesideAFencedBlockIsAFaultAndWritesNoFile()
    {
        Main("Feature: G\n\n" + One);

        IssueScenariosResult result = Apply(Block(Two).Replace("## Scenarios\n\n", "## Scenarios\n\nNone.\n\n", StringComparison.Ordinal));

        Assert.Equal(["The heading ## Scenarios holds the line None. and a fenced block. Keep one of the two."], result.Faults);
        Assert.Equal("Feature: G\n\n" + One, Read());
    }
}
