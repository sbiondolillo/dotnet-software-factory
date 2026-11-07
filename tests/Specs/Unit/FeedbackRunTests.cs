using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackRunTests
{
    private const string A = RunHarness.ThreadA;
    private const string B = RunHarness.ThreadB;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Both(string status = "fixed", string reply = "ok") =>
        RunHarness.Items([(A, status, reply), (B, "fixed", "ok")]);

    private static IEnumerable<string> RepliedIds(RunHarness h) =>
        h.Events.Where(e => e.StartsWith("reply:", StringComparison.Ordinal));

    [Fact]
    public async Task NoThreadsEndsTheRunBeforeAnyCloneContainerOrComment()
    {
        using var h = new RunHarness { Threads = [] };
        Assert.Equal(new FeedbackRunResult(0, 0, 0), await h.RunFeedbackAsync(Ct));
        Assert.Empty(h.Commands);
        Assert.Empty(h.AgentRequests);
        Assert.Empty(h.CommentBodies);
    }

    [Fact]
    public async Task OnlyResolvedThreadsEndTheRunBeforeAnyCloneContainerOrComment()
    {
        using var h = new RunHarness { Threads = [RunHarness.ThreadC] };
        Assert.Equal(new FeedbackRunResult(0, 0, 0), await h.RunFeedbackAsync(Ct));
        Assert.Empty(h.Commands);
        Assert.Empty(h.CommentBodies);
    }

    [Fact]
    public async Task AnAbsentReportIsARedReportThatNamesTheFileAndRunsNoGate()
    {
        using var h = new RunHarness();
        h.Report(Both());
        h.Report(null, 1);
        FeedbackRunResult result = await h.RunFeedbackAsync(Ct);
        Assert.Contains(".factory/threads.json", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
        Assert.Equal(1, result.Redos);
        Assert.Equal(["build", "build", "dotnet", "review"], h.Events.Where(e => e is "build" or "dotnet" or "review"));
        Assert.Contains("- Redo 1, after build 1: The report .factory/threads.json is absent. Write it.", h.Summary.Split('\n'));
        Assert.Equal(2, h.Commands.Count(c => c.Command is "git" && c.Arguments[0] is "commit"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"threads":"x"}""")]
    [InlineData("""{"threads":{}}""")]
    public async Task AReportThatIsNotAnObjectWithAThreadsListIsRed(string text)
    {
        using var h = new RunHarness();
        h.Report(Both());
        h.Report(text, 1);
        FeedbackRunResult result = await h.RunFeedbackAsync(Ct);
        Assert.Equal(1, result.Redos);
        Assert.Contains(".factory/threads.json", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThreadWithNoItemIsRedAndTheTextHoldsItsId()
    {
        using var h = new RunHarness();
        h.Report(Both());
        h.Report(RunHarness.FixedReport(A), 1);
        await h.RunFeedbackAsync(Ct);
        Assert.Contains(B, h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"id":"A","reply":"x"}""", "missing")]
    [InlineData("""{"id":"A","status":"done","reply":"x"}""", "done")]
    [InlineData("""{"id":"A","status":5,"reply":"x"}""", "5")]
    public async Task ABadStatusIsRedAndTheTextHoldsTheIdAndTheStatus(string item, string shown)
    {
        string first = item.Replace("\"A\"", $"\"{A}\"", StringComparison.Ordinal);
        string report = "{\"threads\":[" + first + ",{\"id\":\"" + B + "\",\"status\":\"fixed\",\"reply\":\"ok\"}]}";
        using var h = new RunHarness();
        h.Report(Both());
        h.Report(report, 1);
        await h.RunFeedbackAsync(Ct);
        string text = h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text;
        Assert.Contains(A, text, StringComparison.Ordinal);
        Assert.Contains(shown, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"id":"A","status":"fixed"}""")]
    [InlineData("""{"id":"A","status":"fixed","reply":""}""")]
    [InlineData("""{"id":"A","status":"fixed","reply":" \n "}""")]
    [InlineData("""{"id":"A","status":"fixed","reply":7}""")]
    public async Task ABlankOrMissingReplyIsRedAndTheTextHoldsTheIdAndReply(string item)
    {
        string first = item.Replace("\"A\"", $"\"{A}\"", StringComparison.Ordinal);
        string report = "{\"threads\":[" + first + ",{\"id\":\"" + B + "\",\"status\":\"fixed\",\"reply\":\"ok\"}]}";
        using var h = new RunHarness();
        h.Report(Both());
        h.Report(report, 1);
        FeedbackRunResult result = await h.RunFeedbackAsync(Ct);
        Assert.Equal(1, result.Redos);
        string text = h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text;
        Assert.Contains(A, text, StringComparison.Ordinal);
        Assert.Contains("reply", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoFaultsGiveTwoTextsAndAnItemForAnUnknownIdIsNoFault()
    {
        string report = """{"threads":[{"id":"PRRT_other","status":"fixed","reply":"x"}]}""";
        using var h = new RunHarness();
        h.Report(Both());
        h.Report(report, 1);
        await h.RunFeedbackAsync(Ct);
        string text = h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text;
        Assert.Contains($"no item for the thread {A}", text, StringComparison.Ordinal);
        Assert.Contains($"no item for the thread {B}", text, StringComparison.Ordinal);

        string extra = RunHarness.Items([(A, "fixed", "x"), (B, "fixed", "y"), ("PRRT_other", "done", "")]);
        using var g = new RunHarness();
        g.Report(extra);
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await g.RunFeedbackAsync(Ct));
    }

    [Fact]
    public async Task AFixedItemAndADisputedItemGetAReplyAndAResolveEach()
    {
        string report = RunHarness.Items([(A, "fixed", "fixed it"), (B, "disputed", "the name is right")]);
        using var h = new RunHarness();
        h.Report(report);
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunFeedbackAsync(Ct));
        Assert.Equal(
            ["reply:" + A, "resolve:" + A, "reply:" + B, "resolve:" + B],
            h.Events.Where(e => e.StartsWith("reply:", StringComparison.Ordinal) || e.StartsWith("resolve:", StringComparison.Ordinal)));
        Assert.Contains(h.Requests, r => r.Content is string c && c.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal)
            && c.Contains(B, StringComparison.Ordinal) && c.Contains("the name is right", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStaleReportOfAnEarlierBuildIsNoReport()
    {
        using var h = new RunHarness();
        h.Report(null);
        h.Report(Both(), 1);
        h.RedBuilds.Add(1);
        FeedbackRunResult result = await h.RunFeedbackAsync(Ct);
        Assert.Contains(".factory/threads.json", h.Builder.Of(Phase.Build).ElementAt(2).Prompt.Text, StringComparison.Ordinal);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
    }

    [Fact]
    public async Task ASpentBudgetPostsOneCommentPushesNothingRepliesToNothingAndRemovesTheContainer()
    {
        using var h = new RunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        FeedbackRunResult result = await h.RunFeedbackAsync(Ct);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
        Assert.Single(h.CommentBodies);
        Assert.Contains("3 Redos", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain("push", h.Events);
        Assert.Empty(RepliedIds(h));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task ARoundAppendsItsSectionToTheJobSummaryWithTheSessionsAtTheEnd()
    {
        using var h = new RunHarness();
        h.UseManaged();
        File.WriteAllText(h.SummaryPath!, "## Factory run\n");
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunFeedbackAsync(Ct));
        string summary = h.Summary;
        Assert.StartsWith("## Factory run\n\n## Feedback run\n", summary, StringComparison.Ordinal);
        string section = summary[summary.IndexOf("## Feedback run", StringComparison.Ordinal)..];
        Assert.Equal(
            ["build 1", "lint 1", "test 1", "review 1"],
            section.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Skip(1).Select(line => line.Split('|')[1].Trim()));
        Assert.Matches(@"\n### Sessions\n\n(- sesn_\d+, \w+, \$\d+\.\d\d\n)+$", section);
    }

    [Fact]
    public async Task TwoRoundsAppendTwoSectionsInOrder()
    {
        using var h = new RunHarness();
        await h.RunFeedbackAsync(Ct);
        h.RedBuilds.Add(h.Builds + 1);
        await h.RunFeedbackAsync(Ct);
        string[] headings = [.. h.Summary.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal))];
        Assert.Equal(["## Feedback run", "## Feedback run"], headings);
        Assert.Contains("- Redo 1, after test 1: ", h.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASummaryWriteThatFailsGoesToTheLogAndTheRoundStillPasses()
    {
        using var h = new RunHarness();
        h.SummaryPath = Path.Combine(h.CloneDirectory, "absent", "summary.md");
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunFeedbackAsync(Ct));
        Assert.Contains(h.Log.ToString().Split('\n'), line => line.StartsWith($"summary: cannot write {h.SummaryPath}: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARoundWithNoUnresolvedThreadWritesNoSummary()
    {
        using var h = new RunHarness { Threads = [] };
        await h.RunFeedbackAsync(Ct);
        Assert.False(File.Exists(h.SummaryPath));
    }

    [Fact]
    public async Task AGraphQlErrorOnAReplyThrowsRemovesTheContainerAndSendsNoLaterReplyOrResolve()
    {
        using var h = new RunHarness { ReplyErrorThread = A };
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => h.RunFeedbackAsync(Ct));
        Assert.Equal(["reply:" + A], h.Events.Where(e => e.StartsWith("reply:", StringComparison.Ordinal) || e.StartsWith("resolve:", StringComparison.Ordinal)));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task AHeadBranchReadThatFailsReachesTheCallerBeforeAnyClone()
    {
        using var h = new RunHarness { HeadStatus = 404 };
        await Assert.ThrowsAsync<GitHubRequestException>(() => h.RunFeedbackAsync(Ct));
        Assert.Empty(h.Commands);
    }

    [Fact]
    public async Task ACloneThatFailsReachesTheCallerAndStartsNoContainer()
    {
        using var h = new RunHarness { CloneFails = true };
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunFeedbackAsync(Ct));
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task ACheckoutThatFailsReachesTheCallerAndStartsNoContainer()
    {
        using var h = new RunHarness { CheckoutFailure = "did not match any file(s) known to git" };
        GitCommandException ex = await Assert.ThrowsAsync<GitCommandException>(() => h.RunFeedbackAsync(Ct));
        Assert.Contains("did not match", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task AThreadWithNoLineShowsNoLineInTheBuildPromptAndTheRunGoesOn()
    {
        using var h = new RunHarness { NullLine = true };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunFeedbackAsync(Ct));
        Assert.DoesNotContain("Line: 14", h.Builder.Of(Phase.Build).First().Prompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Line: \n", h.Builder.Of(Phase.Build).First().Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APushThatFailsReachesTheCallerAddsNoReplyAndRemovesTheContainer()
    {
        using var h = new RunHarness { PushFailure = "rejected" };
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunFeedbackAsync(Ct));
        Assert.Empty(RepliedIds(h));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task OnTheManagedPathThePushOfTheSessionBranchComesBeforeEachPrompt()
    {
        using var h = new RunHarness();
        h.UseManaged();
        h.RedBuilds.Add(1);
        await h.RunFeedbackAsync(Ct);

        List<string> steps = [.. h.Timeline.Where(line => IsSessionPush(line) || IsPrompt(line)).Select(line => IsSessionPush(line) ? "push" : "prompt")];
        Assert.Equal(["push", "prompt", "push", "prompt", "push", "prompt"], steps);
    }

    [Fact]
    public async Task OnTheManagedPathABuildThatPushesNoCommitPutsThatFactInTheNextBuildPrompt()
    {
        using var h = new RunHarness();
        h.UseManaged();
        h.Builder.Act(Phase.Build, _ => h.Managed!.HoldHead(), 1);
        h.RedBuilds.Add(1);
        await h.RunFeedbackAsync(Ct);

        List<AgentRequest> builds = [.. h.Builder.Of(Phase.Build)];
        Assert.Equal(2, builds.Count);
        Assert.DoesNotContain(NoCommitText, builds[0].Prompt.Text, StringComparison.Ordinal);
        Assert.Contains(NoCommitText, builds[1].Prompt.Text, StringComparison.Ordinal);
    }

    private const string NoCommitText = "The build pushed no commit to the session branch. Commit the work and push it to that branch.";

    private static bool IsSessionPush(string line) => line.StartsWith("git push origin ", StringComparison.Ordinal) && line.EndsWith("-session", StringComparison.Ordinal) && !line.Contains("--delete", StringComparison.Ordinal);

    /// <summary>A prompt starts a session with its first message, or sends a message to a session that runs.</summary>
    private static bool IsPrompt(string line) =>
        line is "POST /v1/sessions" || (line.StartsWith("POST /v1/sessions/", StringComparison.Ordinal) && line.EndsWith("/events", StringComparison.Ordinal));
}
