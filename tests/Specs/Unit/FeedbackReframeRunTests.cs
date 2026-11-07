using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackReframeRunTests
{
    private const string Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ntrue\n```\n";
    private const string Symbol = "Core.Greeter.Hello()";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FeedbackRunHarness Reframe()
    {
        var h = new FeedbackRunHarness { PullBody = "Closes #11", IssueBody = Body, ApiLines = _ => Symbol + "\n" };
        h.Labels.Add("reframe");
        return h;
    }

    private static IEnumerable<string> RowNames(string summary) =>
        summary.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Skip(1).Select(line => line.Split('|')[1].Trim());

    private static string Row(string summary, string name) =>
        summary.Split('\n').Single(line => line.StartsWith($"| {name} |", StringComparison.Ordinal));

    [Fact]
    public async Task APassingRoundRunsTheApiGateBetweenTheTestGateAndTheCheck()
    {
        using FeedbackRunHarness h = Reframe();
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
        Assert.Equal(["build 1", "lint 1", "scope 1", "test 1", "api 1", "check 1", "review 1"], RowNames(h.Summary));
        Assert.EndsWith("| same |", Row(h.Summary, "api 1"), StringComparison.Ordinal);
        string[] lines = h.Out.ToString().Split('\n');
        Assert.True(Array.IndexOf(lines, "api: listed") < Array.IndexOf(lines, "build: start"));
    }

    [Fact]
    public async Task AChangedPublicApiGivesARedoAfterTheApiRowAndNoCheckOrReviewBeforeIt()
    {
        using FeedbackRunHarness h = Reframe();
        h.ApiLines = build => build is 1 ? "" : Symbol + "\n";
        Assert.Equal(new FeedbackRunResult(2, 0, 1), await h.RunAsync(Ct));
        Assert.Equal(["build 1", "lint 1", "scope 1", "test 1", "api 1", "build 2", "lint 2", "scope 2", "test 2", "api 2", "check 1", "review 1"], RowNames(h.Summary));
        Assert.EndsWith("| changed |", Row(h.Summary, "api 1"), StringComparison.Ordinal);
        Assert.Contains($"- Redo 1, after api 1: lost symbol: {Symbol}", h.Summary.Split('\n'));
        Assert.Contains($"lost symbol: {Symbol}", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABaseWhoseApiCannotBeListedStopsTheRoundBeforeTheBuild()
    {
        using FeedbackRunHarness h = Reframe();
        h.ApiFails = true;
        Assert.Equal(new FeedbackRunResult(0, 2, 0), await h.RunAsync(Ct));
        Assert.Contains("The public API of the base commit could not be listed", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Empty(h.BuildPrompts);
        Assert.DoesNotContain("push", h.Events);
    }
}
