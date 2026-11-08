using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The end and the head that a run gives as a value: the stop of a run with no pass, and the head commit of a pass.</summary>
public sealed class RunOutcomeTests
{
    private const string Pushed = "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeclinedCheckoutGivesTheEndDeclinedWithTheCommentItPosted()
    {
        using var h = new RunHarness { Body = "## Scenarios\n\nThe scenarios follow.\n" };
        RunOutcome<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Null(result.Passed);
        Assert.Equal(new RunStop(Assert.Single(h.CommentBodies), RunEnd.Declined), result.Stop);
    }

    [Fact]
    public async Task ACheckoutStopThatDeclinesNothingGivesTheEndStopped()
    {
        using var h = new RunHarness();
        h.Lacks.Add("Solution.slnx");
        RunOutcome<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Equal(new RunStop(Assert.Single(h.CommentBodies), RunEnd.Stopped), result.Stop);
    }

    [Fact]
    public async Task AGateStopGivesTheEndStoppedWithTheCommentOfTheLoop()
    {
        using var h = new RunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        RunOutcome<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Null(result.Passed);
        Assert.Equal(new RunStop(Assert.Single(h.CommentBodies), RunEnd.Stopped), result.Stop);
        Assert.Equal(3, result.Redos);
    }

    [Fact]
    public async Task AFeedbackRoundThatDeclinesItsClosingIssueGivesTheEndDeclined()
    {
        using var h = new RunHarness { PullBody = "Closes #12" };
        h.Labels.AddRange(["cleanup", "refactor"]);
        RunOutcome<Answered> result = await h.RunFeedbackAsync(Ct);
        Assert.Null(result.Passed);
        Assert.Equal(new RunStop(Assert.Single(h.CommentBodies), RunEnd.Declined), result.Stop);
    }

    [Fact]
    public async Task AFactoryPassGivesTheHeadOfThePushedCommit()
    {
        using var h = new RunHarness();
        h.Builder.Act(Phase.Build, _ => h.HeadSha = Pushed);
        RunOutcome<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Null(result.Stop);
        Assert.Equal(new Delivered(new PullRequest(h.PullNumber, h.PullUrl), Pushed), result.Passed);
    }

    [Fact]
    public async Task AFeedbackPassGivesTheHeadOfThePushedCommit()
    {
        using var h = new RunHarness();
        h.Builder.Act(Phase.Build, _ => h.HeadSha = Pushed);
        RunOutcome<Answered> result = await h.RunFeedbackAsync(Ct);
        Assert.Null(result.Stop);
        Assert.Equal(new Answered(2, 0, Pushed), result.Passed);
    }

    [Fact]
    public async Task AFeedbackRoundThatPushedNothingGivesNoHead()
    {
        using var h = new RunHarness { Threads = [RunHarness.ThreadC] };
        RunOutcome<Answered> result = await h.RunFeedbackAsync(Ct);
        Assert.Equal(new RunOutcome<Answered>(new Answered(0, 0, null), null, 0), result);
        Assert.DoesNotContain("push", h.Events);
    }
}
