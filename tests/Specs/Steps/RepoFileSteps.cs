using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class RepoFileSteps
{
    [Then("the file {string} of this repo holds a line that holds {string}")]
    public static void ThenRepoFileHoldsALineThatHolds(string file, string text)
        => Assert.Contains(Lines(file), line => line.Contains(text, StringComparison.Ordinal));

    [Then("the file {string} of this repo holds the line {string}")]
    public static void ThenRepoFileHoldsTheLine(string file, string text)
        => Assert.Contains(text, Lines(file));

    private static string[] Lines(string file) => File.ReadAllLines(Path.Combine(StarterChecks.RepoRoot(), file));
}
