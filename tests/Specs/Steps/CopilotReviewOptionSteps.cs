using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CopilotReviewOptionSteps(ConsoleProgramSteps console)
{
    /// <summary>The scenario reads the line that the adapter of Claude Code writes before the review prompt, so it runs that adapter.</summary>
    [BeforeScenario]
    [Scope(Feature = "The option --copilot-review turns the Copilot code review request on", Scenario = "run with the review off still runs the review prompt")]
    public void UseClaudeCode() => console.Harness.UseClaudeCode();

    [Then("standard output holds {string}, {string} and {string}")]
    public void ThenOutputThree(string a, string b, string c)
    {
        string output = console.Harness.Out.ToString();
        Assert.Contains(a, output, StringComparison.Ordinal);
        Assert.Contains(b, output, StringComparison.Ordinal);
        Assert.Contains(c, output, StringComparison.Ordinal);
    }

    [Then("standard error is the one line that holds {string}")]
    public void ThenErrorOneLine(string text) =>
        Assert.Contains(text, Assert.Single(Specs.Unit.ConsoleHarness.Lines(console.Harness.Error)), StringComparison.Ordinal);

    [Then("claude gets the review prompt")]
    public void ThenReviewPrompt() => Assert.NotEmpty(console.Harness.Reviewer.Of(Phase.Review));
}
