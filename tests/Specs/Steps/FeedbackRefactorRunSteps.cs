using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeedbackRefactorRunSteps(FactoryRunSteps factory, FeedbackRunSteps feedback)
{
    private FeedbackRunHarness H => feedback.Harness;

    private void SetIssue(int number, string label, string body)
    {
        Assert.Equal(15, number);
        factory.Harness.Labels.Add(label);
        factory.Harness.Body = body;
    }

    private static string CheckBody(string line) => $"## Check\n\n```sh\n{line}\n```\n";

    [Given("the issue {int} has the label {string} and no {string}")]
    public void GivenNoCheck(int number, string label, string heading)
    {
        Assert.Equal("## Check", heading);
        SetIssue(number, label, "Rename Greeting to Greeter.");
    }

    [Given("the issue {int} has the label {string} and the line {string} in the first fenced block under {string}")]
    public void GivenFenced(int number, string label, string line, string heading)
    {
        Assert.Equal("## Check", heading);
        SetIssue(number, label, CheckBody(line));
    }

    [Given("the issue {int} has the label {string} and a {string}")]
    public void GivenCheck(int number, string label, string heading)
    {
        Assert.Equal("## Check", heading);
        SetIssue(number, label, CheckBody("grep -q Greeting src/Core/Greeter.cs"));
    }

    [Given("the build changes {string}")]
    public void GivenChanges(string path) => H.FirstBuildChanges.Add(path);

    [Given("the build adds the file {string}")]
    public void GivenAdds(string path)
    {
        H.FirstBuildChanges.Add(path);
        H.FirstBuildAdds.Add(path);
    }

    [Given("each dotnet command exits with the code 0")]
    public void GivenDotnetPasses() => Assert.Empty(H.RedBuilds);

    [Given("each step passes")]
    public void GivenEachStepPasses() => Assert.Empty(H.RedBuilds);

    [Then("the build prompt holds {string}")]
    public void ThenBuildHolds(string text) => Assert.Contains(text, H.BuildPrompts.First().Text, StringComparison.Ordinal);
}
