using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackRunTests
{
    private const string A = FeedbackRunHarness.ThreadA;
    private const string B = FeedbackRunHarness.ThreadB;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Both(string status = "fixed", string reply = "ok") =>
        FeedbackRunHarness.Items([(A, status, reply), (B, "fixed", "ok")]);

    private static IEnumerable<string> RepliedIds(FeedbackRunHarness h) =>
        h.Events.Where(e => e.StartsWith("reply:", StringComparison.Ordinal));

    [Fact]
    public async Task NoThreadsEndsTheRunBeforeAnyCloneContainerOrComment()
    {
        using var h = new FeedbackRunHarness { Threads = [] };
        Assert.Equal(new FeedbackRunResult(0, 0, 0), await h.RunAsync(Ct));
        Assert.Empty(h.Commands);
        Assert.Empty(h.Prompts);
        Assert.Empty(h.CommentBodies);
    }

    [Fact]
    public async Task OnlyResolvedThreadsEndTheRunBeforeAnyCloneContainerOrComment()
    {
        using var h = new FeedbackRunHarness { Threads = [FeedbackRunHarness.ThreadC] };
        Assert.Equal(new FeedbackRunResult(0, 0, 0), await h.RunAsync(Ct));
        Assert.Empty(h.Commands);
        Assert.Empty(h.CommentBodies);
    }

    [Fact]
    public async Task AnAbsentReportIsARedReportThatNamesTheFileAndRunsNoGate()
    {
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? null : Both() };
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Contains(".factory/threads.json", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
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
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? text : Both() };
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Equal(1, result.Redos);
        Assert.Contains(".factory/threads.json", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThreadWithNoItemIsRedAndTheTextHoldsItsId()
    {
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? FeedbackRunHarness.FixedReport(A) : Both() };
        await h.RunAsync(Ct);
        Assert.Contains(B, h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"id":"A","reply":"x"}""", "missing")]
    [InlineData("""{"id":"A","status":"done","reply":"x"}""", "done")]
    [InlineData("""{"id":"A","status":5,"reply":"x"}""", "5")]
    public async Task ABadStatusIsRedAndTheTextHoldsTheIdAndTheStatus(string item, string shown)
    {
        string first = item.Replace("\"A\"", $"\"{A}\"", StringComparison.Ordinal);
        string report = "{\"threads\":[" + first + ",{\"id\":\"" + B + "\",\"status\":\"fixed\",\"reply\":\"ok\"}]}";
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? report : Both() };
        await h.RunAsync(Ct);
        string text = h.BuildPrompts.ElementAt(1).Text;
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
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? report : Both() };
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Equal(1, result.Redos);
        string text = h.BuildPrompts.ElementAt(1).Text;
        Assert.Contains(A, text, StringComparison.Ordinal);
        Assert.Contains("reply", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoFaultsGiveTwoTextsAndAnItemForAnUnknownIdIsNoFault()
    {
        string report = """{"threads":[{"id":"PRRT_other","status":"fixed","reply":"x"}]}""";
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? report : Both() };
        await h.RunAsync(Ct);
        string text = h.BuildPrompts.ElementAt(1).Text;
        Assert.Contains($"no item for the thread {A}", text, StringComparison.Ordinal);
        Assert.Contains($"no item for the thread {B}", text, StringComparison.Ordinal);

        string extra = FeedbackRunHarness.Items([(A, "fixed", "x"), (B, "fixed", "y"), ("PRRT_other", "done", "")]);
        using var g = new FeedbackRunHarness { Report = _ => extra };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await g.RunAsync(Ct));
    }

    [Fact]
    public async Task AFixedItemAndADisputedItemGetAReplyAndAResolveEach()
    {
        string report = FeedbackRunHarness.Items([(A, "fixed", "fixed it"), (B, "disputed", "the name is right")]);
        using var h = new FeedbackRunHarness { Report = _ => report };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
        Assert.Equal(
            ["reply:" + A, "resolve:" + A, "reply:" + B, "resolve:" + B],
            h.Events.Where(e => e.StartsWith("reply:", StringComparison.Ordinal) || e.StartsWith("resolve:", StringComparison.Ordinal)));
        Assert.Contains(h.Requests, r => r.Content is string c && c.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal)
            && c.Contains(B, StringComparison.Ordinal) && c.Contains("the name is right", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStaleReportOfAnEarlierBuildIsNoReport()
    {
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? Both() : null };
        h.RedBuilds.Add(1);
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Contains(".factory/threads.json", h.BuildPrompts.ElementAt(2).Text, StringComparison.Ordinal);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
    }

    [Fact]
    public async Task ASpentBudgetPostsOneCommentPushesNothingRepliesToNothingAndRemovesTheContainer()
    {
        using var h = new FeedbackRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        FeedbackRunResult result = await h.RunAsync(Ct);
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
        using var h = new FeedbackRunHarness();
        h.UseManaged();
        File.WriteAllText(h.SummaryPath, "## Factory run\n");
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
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
        using var h = new FeedbackRunHarness();
        await h.RunAsync(Ct);
        h.RedBuilds.Add(h.Builds + 1);
        await h.RunAsync(Ct);
        string[] headings = [.. h.Summary.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal))];
        Assert.Equal(["## Feedback run", "## Feedback run"], headings);
        Assert.Contains("- Redo 1, after test 1: ", h.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASummaryWriteThatFailsGoesToTheLogAndTheRoundStillPasses()
    {
        using var h = new FeedbackRunHarness();
        h.SummaryPath = Path.Combine(h.CloneDirectory, "absent", "summary.md");
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
        Assert.StartsWith($"summary: cannot write {h.SummaryPath}: ", h.Log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARoundWithNoUnresolvedThreadWritesNoSummary()
    {
        using var h = new FeedbackRunHarness { Threads = [] };
        await h.RunAsync(Ct);
        Assert.False(File.Exists(h.SummaryPath));
    }

    [Fact]
    public async Task AGraphQlErrorOnAReplyThrowsRemovesTheContainerAndSendsNoLaterReplyOrResolve()
    {
        using var h = new FeedbackRunHarness { ReplyErrorThread = A };
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => h.RunAsync(Ct));
        Assert.Equal(["reply:" + A], h.Events.Where(e => e.StartsWith("reply:", StringComparison.Ordinal) || e.StartsWith("resolve:", StringComparison.Ordinal)));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task AHeadBranchReadThatFailsReachesTheCallerBeforeAnyClone()
    {
        using var h = new FeedbackRunHarness { HeadStatus = 404 };
        await Assert.ThrowsAsync<GitHubRequestException>(() => h.RunAsync(Ct));
        Assert.Empty(h.Commands);
    }

    [Fact]
    public async Task ACloneThatFailsReachesTheCallerAndStartsNoContainer()
    {
        using var h = new FeedbackRunHarness { CloneFails = true };
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunAsync(Ct));
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task ACheckoutThatFailsReachesTheCallerAndStartsNoContainer()
    {
        using var h = new FeedbackRunHarness { CheckoutFailure = "did not match any file(s) known to git" };
        GitCommandException ex = await Assert.ThrowsAsync<GitCommandException>(() => h.RunAsync(Ct));
        Assert.Contains("did not match", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task AThreadWithNoLineShowsNoLineInTheBuildPromptAndTheRunGoesOn()
    {
        using var h = new FeedbackRunHarness { NullLine = true };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
        Assert.DoesNotContain("Line: 14", h.BuildPrompts.First().Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Line: \n", h.BuildPrompts.First().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APushThatFailsReachesTheCallerAddsNoReplyAndRemovesTheContainer()
    {
        using var h = new FeedbackRunHarness { PushFailure = "rejected" };
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunAsync(Ct));
        Assert.Empty(RepliedIds(h));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task OnTheManagedPathThePushOfTheSessionBranchComesBeforeEachPrompt()
    {
        using var h = new FeedbackRunHarness();
        h.UseManaged();
        h.RedBuilds.Add(1);
        await h.RunAsync(Ct);

        List<string> steps = [.. h.Timeline.Where(line => IsSessionPush(line) || IsPrompt(line)).Select(line => IsSessionPush(line) ? "push" : "prompt")];
        Assert.Equal(["push", "prompt", "push", "prompt", "push", "prompt"], steps);
    }

    [Fact]
    public async Task OnTheManagedPathABuildThatPushesNoCommitPutsThatFactInTheNextBuildPrompt()
    {
        using var h = new FeedbackRunHarness();
        h.UseManaged();
        h.NoPushBuilds.Add(1);
        h.RedBuilds.Add(1);
        await h.RunAsync(Ct);

        List<SeenPrompt> builds = [.. h.BuildPrompts];
        Assert.Equal(2, builds.Count);
        Assert.DoesNotContain(NoCommitText, builds[0].Text, StringComparison.Ordinal);
        Assert.Contains(NoCommitText, builds[1].Text, StringComparison.Ordinal);
    }

    private const string NoCommitText = "The build pushed no commit to the session branch. Commit the work and push it to that branch.";

    private static bool IsSessionPush(string line) => line.StartsWith("git push origin ", StringComparison.Ordinal) && line.EndsWith("-session", StringComparison.Ordinal) && !line.Contains("--delete", StringComparison.Ordinal);

    /// <summary>A prompt starts a session with its first message, or sends a message to a session that runs.</summary>
    private static bool IsPrompt(string line) =>
        line is "POST /v1/sessions" || (line.StartsWith("POST /v1/sessions/", StringComparison.Ordinal) && line.EndsWith("/events", StringComparison.Ordinal));
}
