using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class StorySkillSteps
{
    [Then("the file {string} of this repo holds a line that holds {string}")]
    public static void ThenRepoFileHoldsALineThatHolds(string file, string text)
        => Assert.Contains(Lines(file), line => line.Contains(text, StringComparison.Ordinal));

    [Then("the file {string} of this repo holds no line that holds {string}")]
    public static void ThenRepoFileHoldsNoLineThatHolds(string file, string text)
        => Assert.DoesNotContain(Lines(file), line => line.Contains(text, StringComparison.Ordinal));

    private static string[] Lines(string file) => File.ReadAllLines(Path.Combine(StarterChecks.RepoRoot(), file));
}
