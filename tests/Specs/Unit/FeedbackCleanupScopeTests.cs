using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackCleanupScopeTests
{
    private const string CheckBody = "## Scope\nREADME.md\n\n## Check\n\n```sh\ngrep -q Feedback README.md\n```\n";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FeedbackRunHarness Cleanup(string body = CheckBody)
    {
        var h = new FeedbackRunHarness { PullBody = "Closes #11", IssueBody = body };
        h.Labels.Add("cleanup");
        return h;
    }

    private static bool RunsCheck(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        int id = arguments.IndexOf(FeedbackRunHarness.ContainerId);
        return id >= 0 && arguments.Skip(id + 1).Take(2).SequenceEqual(["sh", "-c"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Closes #")]
    [InlineData("Closes #abc")]
    [InlineData("See the issue")]
    public void ABodyWithNoClosingLineNamesNoIssue(string? body) => Assert.Null(PullRequests.ParseClosingIssue(body));

    [Fact]
    public void AClosingLineAmongOtherTextNamesItsIssue() =>
        Assert.Equal(11, PullRequests.ParseClosingIssue("Answers the review.\n\nCloses #11\nThanks"));

    [Fact]
    public async Task AnIssueWithoutTheLabelRunsNoCheckAndAppliesNoScope()
    {
        using FeedbackRunHarness h = Cleanup();
        h.Labels.Clear();
        h.Labels.Add("bug");
        h.FirstBuildChanges.Add("src/Core/Greeter.cs");
        await h.RunAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.Equal(2, h.Prompts.Count);
        Assert.Contains(h.Prompts, p => p.Kind is "review");
    }

    [Fact]
    public async Task AFailedIssueReadSurfacesAndStartsNoBuild()
    {
        using FeedbackRunHarness h = Cleanup();
        h.IssueStatus = 404;
        await Assert.ThrowsAsync<GitHubRequestException>(() => h.RunAsync(Ct));
        Assert.Empty(h.Prompts);
    }

    [Fact]
    public async Task AChangeOutsideTheScopeGivesARedoAndNoTestGate()
    {
        using FeedbackRunHarness h = Cleanup();
        h.FirstBuildChanges.Add("src/Core/Greeter.cs");
        await h.RunAsync(Ct);
        Assert.Contains("outside the scope: src/Core/Greeter.cs", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
        Assert.Equal("test: start", h.Out.ToString().Split('\n').First(l => l.StartsWith("test:", StringComparison.Ordinal)));
        Assert.Equal(1, h.Out.ToString().Split('\n').Count(l => l is "test: start"));
    }

    [Fact]
    public async Task AChangeToThePinnedFeaturesGivesAPinRedo()
    {
        using FeedbackRunHarness h = Cleanup();
        h.FirstBuildChanges.Add("specs/features/a.feature");
        await h.RunAsync(Ct);
        Assert.Contains("pin: specs/features/a.feature", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeToASettingsFileTheScopeNamesPassesTheTestGate()
    {
        using FeedbackRunHarness h = Cleanup("## Scope\n.editorconfig\n");
        h.FirstBuildChanges.Add(".editorconfig");
        h.FirstBuildWrites.Add(".editorconfig");
        FeedbackRunResult result = await h.RunAsync(Ct);
        string[] lines = h.Out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("test: passed", lines);
        Assert.DoesNotContain("test: failed", lines);
        Assert.Equal(new FeedbackRunResult(2, 0, 0), result);
    }

    [Fact]
    public async Task ALostTestGivesARedoAndNoCheck()
    {
        using FeedbackRunHarness h = Cleanup();
        h.BaseOnlyTests.Add(("Specs.Unit.GreeterTests", "Greets"));
        h.CheckResults[1] = (1, "", "");
        await h.RunAsync(Ct);
        Assert.Contains("lost test: Specs.Unit.GreeterTests.Greets", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("check: red", h.Out.ToString().Split('\n').Take(Array.IndexOf(h.Out.ToString().Split('\n'), "redo: 1")));
    }

    [Fact]
    public async Task ARedTestGateGivesItsEvidenceAndNoCheck()
    {
        using FeedbackRunHarness h = Cleanup();
        h.RedBuilds.Add(1);
        await h.RunAsync(Ct);
        Assert.Contains(FeedbackRunHarness.RedText, h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("check: red", h.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingCheckGivesARedoAndNoReview()
    {
        using FeedbackRunHarness h = Cleanup();
        h.CheckResults[1] = (1, "out", "err");
        await h.RunAsync(Ct);
        Assert.Contains("The check command exited with the code 1", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
        Assert.Contains("check: red", h.Out.ToString(), StringComparison.Ordinal);
        Assert.Equal("build", h.Prompts[1].Kind);
    }

    [Fact]
    public async Task ACheckThatFailsOnEveryBuildSpendsTheBudgetAndPostsTheComment()
    {
        using FeedbackRunHarness h = Cleanup();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.CheckResults[build] = (1, "", "red");
        }

        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
        Assert.Contains("The budget of 3 Redos is spent", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "review");
    }

    [Fact]
    public async Task ABodyWithNoCheckBlockRunsNoCheckAndStartsTheReview()
    {
        using FeedbackRunHarness h = Cleanup("## Scope\nREADME.md\n");
        await h.RunAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.Contains(h.Prompts, p => p.Kind is "review");
    }
}
