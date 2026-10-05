using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CleanupListScopeSteps
{
    private string _readme = "";

    [Given("the README of the repo")]
    public void GivenTheReadme() => _readme = File.ReadAllText(Path.Combine(StarterChecks.RepoRoot(), "README.md"));

    [Then("the README holds the paragraph {string}")]
    public void ThenTheReadmeHolds(string paragraph) =>
        Assert.Contains(paragraph, _readme.Split('\n'));
}
