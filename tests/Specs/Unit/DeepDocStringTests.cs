using Core;
using Xunit;

namespace Specs.Unit;

public sealed class DeepDocStringTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private IReadOnlyList<int> Deep(params string[] docLines)
    {
        string path = Path.Combine(_dir, "a.feature");
        File.WriteAllText(path, "Feature: F\n\n  Scenario: S\n    Given x\n" + string.Join('\n', docLines) + "\n");
        return FeatureFiles.ReadDeepDocStringLines(path);
    }

    [Fact]
    public void DeepQuotesAreReported() =>
        Assert.Equal([5], Deep("      \"\"\"", "        t", "      \"\"\""));

    [Fact]
    public void DeepBackticksAreReported() =>
        Assert.Equal([5], Deep("      ```", "        t", "      ```"));

    [Fact]
    public void MixedIndentIsAccepted() =>
        Assert.Empty(Deep("      \"\"\"", "      a", "        b", "      \"\"\""));

    [Fact]
    public void EmptyAndBlankAreAccepted()
    {
        Assert.Empty(Deep("      \"\"\"", "      \"\"\""));
        Assert.Empty(Deep("      \"\"\"", "", "      \"\"\""));
    }

    [Fact]
    public void TwoDocStringsReportOnlyTheDeepOne() =>
        Assert.Equal([9], Deep("      \"\"\"", "      a", "      \"\"\"", "    And y", "      \"\"\"", "        b", "      \"\"\""));

    [Fact]
    public void DeepDocStringStillReadsForTitlesAndSteps()
    {
        Deep("      \"\"\"", "        t", "      \"\"\"");
        string path = Path.Combine(_dir, "a.feature");
        Assert.Equal(["S"], FeatureFiles.ReadScenarioTitles(path));
        Assert.Single(FeatureFiles.ReadScenarioSteps(path, File.ReadAllText(path)));
    }
}
