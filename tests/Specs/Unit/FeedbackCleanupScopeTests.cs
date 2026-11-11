using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackCleanupScopeTests
{
    private const string CheckBody = "## Scope\nREADME.md\n\n## Check\n\n```sh\ngrep -q Feedback README.md\n```\n";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RunHarness Cleanup(string body = CheckBody)
    {
        var h = new RunHarness { ClosingIssue = 11, Body = body };
        h.Labels.Add("cleanup");
        return h;
    }

    private static bool RunsCheck(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        int id = arguments.IndexOf(GateSandbox.ContainerId);
        return id >= 0 && arguments.Skip(id + 1).Take(2).SequenceEqual(["sh", "-c"]);
    }

    [Fact]
    public async Task AnIssueWithoutTheLabelRunsNoCheckAndAppliesNoScope()
    {
        using RunHarness h = Cleanup();
        h.Labels.Clear();
        h.Labels.Add("bug");
        h.FirstBuildChanges.Add("src/Core/Greeter.cs");
        await h.RunFeedbackAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.Equal(2, h.AgentRequests.Count);
        Assert.NotEmpty(h.Reviewer.Of(Phase.Review));
    }

    [Fact]
    public async Task AFailedIssueReadSurfacesAndStartsNoBuild()
    {
        using RunHarness h = Cleanup();
        h.IssueStatus = 404;
        await Assert.ThrowsAsync<GitHubRequestException>(() => h.RunFeedbackAsync(Ct));
        Assert.Empty(h.AgentRequests);
    }

    [Fact]
    public async Task AChangeOutsideTheScopeGivesARedoAndNoTestGate()
    {
        using RunHarness h = Cleanup();
        h.FirstBuildChanges.Add("src/Core/Greeter.cs");
        await h.RunFeedbackAsync(Ct);
        Assert.Contains("outside the scope: src/Core/Greeter.cs", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
        string[] lines = h.Progress.ToString().Split('\n');
        Assert.Equal(["scope: start", "scope: out of scope"], lines.Where(l => l.StartsWith("scope:", StringComparison.Ordinal)).Take(2));
        Assert.Equal("test: start", h.Progress.ToString().Split('\n').First(l => l.StartsWith("test:", StringComparison.Ordinal)));
        Assert.Equal(1, h.Progress.ToString().Split('\n').Count(l => l is "test: start"));
    }

    [Fact]
    public async Task AChangeToThePinnedFeaturesGivesAPinRedo()
    {
        using RunHarness h = Cleanup();
        h.FirstBuildChanges.Add("specs/features/a.feature");
        await h.RunFeedbackAsync(Ct);
        Assert.Contains("pin: specs/features/a.feature", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeToASettingsFileTheScopeNamesPassesTheTestGate()
    {
        using RunHarness h = Cleanup("## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n");
        h.FirstBuildChanges.Add(".editorconfig");
        h.BuildWrites(1, ".editorconfig");
        RunResult<Answered> result = await h.RunFeedbackAsync(Ct);
        string[] lines = h.Progress.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("test: passed", lines);
        Assert.DoesNotContain("test: failed", lines);
        Assert.Equal(new RunResult<Answered>(new Answered(2, 0, h.HeadSha), null, 0), result);
    }

    [Fact]
    public async Task ALostTestGivesARedoAndNoCheck()
    {
        using RunHarness h = Cleanup();
        h.BaseOnlyTests.Add(("Specs.Unit.GreeterTests", "Greets"));
        h.CheckResults[1] = (1, "", "");
        await h.RunFeedbackAsync(Ct);
        Assert.Contains("lost test: Specs.Unit.GreeterTests.Greets", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("check: red", h.Progress.ToString().Split('\n').Take(Array.IndexOf(h.Progress.ToString().Split('\n'), "redo: 1")));
    }

    [Fact]
    public async Task AFailingCheckGivesARedoAndNoReview()
    {
        using RunHarness h = Cleanup();
        h.CheckResults[1] = (1, "out", "err");
        await h.RunFeedbackAsync(Ct);
        Assert.Contains("The check command exited with the code 1", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
        Assert.Contains("check: red", h.Progress.ToString(), StringComparison.Ordinal);
        Assert.Equal(Phase.Build, h.AgentRequests[1].Phase);
    }

    [Fact]
    public async Task ABodyWithNoCheckBlockDeclinesTheRoundWithTheDeclineText()
    {
        using RunHarness h = Cleanup("## Scope\nREADME.md\n");
        RunResult<Answered> result = await h.RunFeedbackAsync(Ct);
        Assert.Equal(RecipeRules.Parse(["cleanup"], "## Scope\nREADME.md\n").Declined, Assert.Single(h.CommentBodies));
        Assert.Null(result.Passed);
        Assert.Equal(new RunStop(Assert.Single(h.CommentBodies), RunEnd.Declined), result.Stop);
        Assert.Equal(0, result.Redos);
        Assert.Empty(h.AgentRequests);
        Assert.DoesNotContain(h.Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
    }
}
