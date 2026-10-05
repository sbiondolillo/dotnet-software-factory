using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CleanupScopeListSteps
{
    [Then("the section states that a scope line is a path, bare or as a Markdown list item with the marker {string}, {string} or {string}, and that the path can sit in backticks {string}")]
    public static void ThenSectionStatesForms(string dash, string star, string plus, string tick)
    {
        string section = WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), "## Start a new repo");
        string sentence = Assert.Single(section.Split('\n'), l => l.Contains("## Scope", StringComparison.Ordinal));
        Assert.Contains("list item", sentence, StringComparison.Ordinal);
        Assert.Contains("backtick", sentence, StringComparison.Ordinal);
        foreach (string marker in new[] { dash, star, plus, tick })
        {
            Assert.Contains(marker, sentence, StringComparison.Ordinal);
        }
    }
}
