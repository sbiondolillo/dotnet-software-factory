using System.Text;
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
        Assert.Contains(".factory/threads.json", h.BuildPrompts.ElementAt(1).Text);
        Assert.Equal(1, result.Redos);
        Assert.Equal(["build", "build", "dotnet", "review"], h.Events.Where(e => e is "build" or "dotnet" or "review"));
        Assert.Single(h.Commands, c => c.Command is "git" && c.Arguments[0] is "commit");
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
        Assert.Contains(".factory/threads.json", h.BuildPrompts.ElementAt(1).Text);
    }

    [Fact]
    public async Task AThreadWithNoItemIsRedAndTheTextHoldsItsId()
    {
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? FeedbackRunHarness.FixedReport(A) : Both() };
        await h.RunAsync(Ct);
        Assert.Contains(B, h.BuildPrompts.ElementAt(1).Text);
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
        Assert.Contains(A, text);
        Assert.Contains(shown, text);
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
        Assert.Contains(A, text);
        Assert.Contains("reply", text);
    }

    [Fact]
    public async Task TwoFaultsGiveTwoTextsAndAnItemForAnUnknownIdIsNoFault()
    {
        string report = """{"threads":[{"id":"PRRT_other","status":"fixed","reply":"x"}]}""";
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? report : Both() };
        await h.RunAsync(Ct);
        string text = h.BuildPrompts.ElementAt(1).Text;
        Assert.Contains($"no item for the thread {A}", text);
        Assert.Contains($"no item for the thread {B}", text);

        string extra = FeedbackRunHarness.Items([(A, "fixed", "x"), (B, "fixed", "y"), ("PRRT_other", "done", "")]);
        using var g = new FeedbackRunHarness { Report = _ => extra };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await g.RunAsync(Ct));
    }

    [Fact]
    public async Task AStaleReportOfAnEarlierBuildIsNoReport()
    {
        using var h = new FeedbackRunHarness { Report = n => n is 1 ? Both() : null };
        h.RedBuilds.Add(1);
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Contains(".factory/threads.json", h.BuildPrompts.ElementAt(2).Text);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
    }

    [Fact]
    public async Task RedReportsAndRedGatesShareOneBudgetOfThree()
    {
        using var h = new FeedbackRunHarness { Report = n => n is 2 or 4 ? null : Both() };
        h.RedBuilds.Add(1);
        h.RedBuilds.Add(3);
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Equal(4, h.Builds);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
        Assert.Single(h.CommentBodies);
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
        Assert.Contains("3 Redos", Assert.Single(h.CommentBodies));
        Assert.DoesNotContain("push", h.Events);
        Assert.Empty(RepliedIds(h));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task ARejectingFourthReviewPostsItsBlockingItems()
    {
        using var h = new FeedbackRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RejectedBuilds.Add(build);
        }

        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
        Assert.Contains(FeedbackRunHarness.Blocking, Assert.Single(h.CommentBodies));
        Assert.DoesNotContain("push", h.Events);
    }

    [Fact]
    public async Task ARedReportAfterTheFourthBuildPostsOneComment()
    {
        using var h = new FeedbackRunHarness { Report = _ => null };
        FeedbackRunResult result = await h.RunAsync(Ct);
        Assert.Equal(new FeedbackRunResult(0, 2, 3), result);
        Assert.Contains(".factory/threads.json", Assert.Single(h.CommentBodies));
        Assert.DoesNotContain("dotnet", h.Events);
    }

    [Fact]
    public async Task ACommentOverTheLimitIsCutToTheLimitInBytesAndAKeptTextStartsWholeAndEndsWithTheMarker()
    {
        using var h = new FeedbackRunHarness { TestStdout = new string('é', 200000) };
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 262144);
        Assert.Contains("[cut]", body);
        Assert.DoesNotContain('\uFFFD', body);
        Assert.Contains(new string('é', 1000), body);
    }

    [Fact]
    public async Task ACommentWithinTheLimitIsPostedWhole()
    {
        using var h = new FeedbackRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        await h.RunAsync(Ct);
        Assert.DoesNotContain("[cut]", Assert.Single(h.CommentBodies));
    }

    [Fact]
    public async Task AnErrorFlagWithAFaultFreeReportStillGoesToTheGates()
    {
        using var h = new FeedbackRunHarness { BuildIsError = true };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
        Assert.Contains("dotnet", h.Events);
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
        Assert.Contains("did not match", ex.Message);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task AThreadWithNoLineShowsNoLineInTheBuildPromptAndTheRunGoesOn()
    {
        using var h = new FeedbackRunHarness { NullLine = true };
        Assert.Equal(new FeedbackRunResult(2, 0, 0), await h.RunAsync(Ct));
        Assert.DoesNotContain("Line: 14", h.BuildPrompts.First().Text);
        Assert.DoesNotContain("Line: \n", h.BuildPrompts.First().Text);
    }

    [Fact]
    public async Task APushThatFailsReachesTheCallerAddsNoReplyAndRemovesTheContainer()
    {
        using var h = new FeedbackRunHarness { PushFailure = "rejected" };
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunAsync(Ct));
        Assert.Empty(RepliedIds(h));
        Assert.Contains("rm", h.Events);
    }
}
