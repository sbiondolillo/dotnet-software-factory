using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CopilotReviewOptionSteps(ConsoleProgramSteps console)
{
    [Then("standard output holds {string}, {string} and {string}")]
    public void ThenOutputThree(string a, string b, string c)
    {
        string output = console.Harness.Out.ToString();
        Assert.Contains(a, output);
        Assert.Contains(b, output);
        Assert.Contains(c, output);
    }

    [Then("standard error is the one line that holds {string}")]
    public void ThenErrorOneLine(string text) =>
        Assert.Contains(text, Assert.Single(Specs.Unit.ConsoleHarness.Lines(console.Harness.Error)));

    [Then("claude gets the review prompt")]
    public void ThenReviewPrompt() => Assert.Contains(console.Harness.Prompts, p => p.Kind is "review");
}
