using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CopilotReviewRequestSteps
{
    private string _section = "";

    [When("a reader reads the section {string} of README.md")]
    public void WhenReaderReads(string heading) =>
        _section = WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), heading);

    [Then("the section holds the line {string}")]
    public void ThenHolds(string line) => Assert.Contains(line, _section.Split('\n'));

    [Then("the section holds no line {string}")]
    public void ThenHoldsNo(string line) => Assert.DoesNotContain(line, _section.Split('\n'));
}
