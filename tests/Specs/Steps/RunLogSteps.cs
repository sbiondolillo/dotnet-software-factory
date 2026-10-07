using System.Globalization;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class RunLogSteps(ConsoleProgramSteps console)
{
    private const string CleanupBody = "Tidy the code.\n\n## Scope\n\nsrc/\n\n## Check\n\n```\ntrue\n```\n";

    private ConsoleHarness H => console.Harness;

    private static TimeSpan Seconds(string text) => TimeSpan.FromSeconds(double.Parse(text, CultureInfo.InvariantCulture));

    [Given("claude answers the plan prompt with {int} turns and the cost {word} after {word} seconds")]
    public void GivenPlanAnswer(int turns, string cost, string seconds) => H.Answers["plan"] = (turns, cost, Seconds(seconds));

    [Given("claude answers a build prompt with {int} turns and the cost {word} after {word} seconds")]
    public void GivenBuildAnswer(int turns, string cost, string seconds) => H.Answers["build"] = (turns, cost, Seconds(seconds));

    [Given("claude answers a review prompt with {int} turns and the cost {word} after {word} seconds")]
    public void GivenReviewAnswer(int turns, string cost, string seconds) => H.Answers["review"] = (turns, cost, Seconds(seconds));

    [Given("every dotnet command in the container exits with the code {int}")]
    public static void GivenEveryDotnet(int code) => Assert.Equal(0, code);

    [Given("the dotnet command {string} exits with the code {int}")]
    public void GivenDotnetExit(string command, int code) => H.DotnetExits[command] = code;

    [Given("the issue has the label {string} and the check exits with the code {int}")]
    public void GivenCleanup(string label, int code)
    {
        H.Labels.Add(label);
        H.IssueBody = CleanupBody;
        H.CheckResults[0] = (code, "", "");
    }

    [Given("the issue has the label {string} and a {string} and the check exits with the code {int}")]
    public void GivenRefactor(string label, string heading, int code)
    {
        H.Labels.Add(label);
        H.IssueBody = $"Tidy the code.\n\n{heading}\n\n```\ntrue\n```\n";
        H.CheckResults[0] = (code, "", "");
    }

    [Then("when claude gets the plan prompt standard error already holds the line {string}")]
    public void ThenPlanStart(string line) => AlreadyHolds("plan", line);

    [Then("when claude gets a build prompt standard error already holds the line {string}")]
    public void ThenBuildStart(string line) => AlreadyHolds("build", line);

    [Then("when claude gets a review prompt standard error already holds the line {string}")]
    public void ThenReviewStart(string line) => AlreadyHolds("review", line);

    [Then("the end line of the plan prompt ends with {string}")]
    public void ThenPlanEndEnds(string ending) =>
        Assert.EndsWith(ending, Assert.Single(ConsoleHarness.Lines(H.Error), l => l.StartsWith("agent: plan end", StringComparison.Ordinal)), StringComparison.Ordinal);

    [Then("standard error holds these command lines in this order")]
    public void ThenCommandLines(Table table)
    {
        List<string> lines = [.. ConsoleHarness.Lines(H.Error)];
        int at = 0;
        foreach (var row in table.Rows)
        {
            string[] parts = row[0].Split("<any>");
            int found = lines.FindIndex(at, l => l.StartsWith(parts[0], StringComparison.Ordinal) && l.EndsWith(parts[^1], StringComparison.Ordinal));
            Assert.True(found >= 0, $"no line {row[0]} after line {at}");
            at = found + 1;
        }
    }

    private void AlreadyHolds(string kind, string line)
    {
        int index = H.Prompts.FindIndex(p => p.Kind == kind);
        Assert.True(index >= 0, $"claude got no {kind} prompt");
        Assert.Contains(line, H.ErrorAtPrompt[index].Split('\n'));
    }
}
