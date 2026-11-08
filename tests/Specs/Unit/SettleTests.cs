using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The lifecycle of a pull request: the opening run, the head wait, the watch, the feedback rounds and their stop rule,
/// and the progress lines, over <see cref="SettleHarness"/>.</summary>
public sealed class SettleTests : IDisposable
{
    private const string OldHead = "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2";
    private readonly SettleHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<SettleResult> IssueAsync(int rounds = 3, bool copilotReview = false, int timeoutMinutes = 60) =>
        _h.SettleIssueAsync(SettleHarness.Options(rounds, copilotReview, timeoutMinutes), cancellationToken: Token);

    private Task<SettleResult> PullRequestAsync(int timeoutMinutes = 60) =>
        _h.SettlePullRequestAsync(SettleHarness.Options(0, false, timeoutMinutes), Token);

    private IEnumerable<string> Out => SettleHarness.Lines(_h.Out);

    private int Watches => Out.Count(line => line is "watch: start");

    private IEnumerable<TimedRequest> ReviewRequests =>
        _h.Requests.Where(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));

    private int FeedbackRuns => _h.Commands.Count(c => c.Command is "git" && c.Arguments[0] is "clone") - 1;

    [Fact]
    public async Task ACleanFirstWatchSettlesWithNoFeedbackRun()
    {
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Settled, result.End);
        Assert.Equal(0, result.Rounds);
        Assert.Equal(0, FeedbackRuns);
        Assert.Equal(_h.PullUrl, Out.First(line => line.StartsWith("https://", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARunThatGivesNoPullRequestStopsWithNoWatchAndNoResultLine()
    {
        _h.Lacks.Add("Solution.slnx");
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Stopped, result.End);
        Assert.Null(result.Last);
        Assert.Equal(0, result.Rounds);
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.DoesNotContain("checks:", _h.Out.ToString(), StringComparison.Ordinal);
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task ADeclinedIssueEndsDeclinedWithNoClone()
    {
        _h.Labels.AddRange(["cleanup", "refactor"]);
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Declined, result.End);
        Assert.Null(result.Last);
        Assert.Equal(0, result.Rounds);
        Assert.Empty(_h.CloneDirectories);
    }

    [Fact]
    public async Task ARefusalAfterTheCloneThrowsAndRemovesTheWorkTree()
    {
        _h.Refusal = ("/pulls", 422);
        GitHubRequestException refused = await Assert.ThrowsAsync<GitHubRequestException>(() => IssueAsync(timeoutMinutes: 0));
        Assert.Equal(422, refused.StatusCode);
        Assert.NotEmpty(_h.CloneDirectories);
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task AHeadThatStaysOldStartsTheWatchOnItOnlyAfter10Seconds()
    {
        _h.HeadSha = _ => OldHead;
        _h.Reviews = SettleHarness.ReviewsJson("copilot-pull-request-reviewer", OldHead);
        Assert.Equal(SettleEnd.Settled, (await IssueAsync()).End);
        TimedRequest read = _h.Requests.First(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.True(read.At - _h.PushTimes[0] >= TimeSpan.FromSeconds(10));
        Assert.True(read.At - _h.PushTimes[0] < TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task AHeadThatChangesWithin10SecondsStartsTheWatchOnTheNewCommitAtOnce()
    {
        _h.HeadSha = read => read < 4 ? OldHead : SettleHarness.DefaultSha;
        Assert.Equal(SettleEnd.Settled, (await IssueAsync()).End);
        TimedRequest read = _h.Requests.First(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.Contains(SettleHarness.DefaultSha, read.Path, StringComparison.Ordinal);
        Assert.True(read.At - _h.PushTimes[0] < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AHeadThatARoundPushedAndGitHubReadsLateStartsTheNextWatchOnlyAfter10Seconds()
    {
        _h.Threads = "always";
        _h.HeadSha = _ => _h.PushTimes.Count > 1 ? OldHead : SettleHarness.DefaultSha;
        await IssueAsync(rounds: 1);
        Assert.Equal(2, _h.PushTimes.Count);
        TimedRequest read = _h.Requests.Where(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal)).ElementAt(1);
        Assert.True(read.At - _h.PushTimes[1] >= TimeSpan.FromSeconds(10));
        Assert.True(read.At - _h.PushTimes[1] < TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task AHeadThatThePullRequestsOpeningRunPushedAndGitHubReadsLateStartsTheWatchOnlyAfter10Seconds()
    {
        _h.Threads = "always";
        _h.HeadSha = _ => OldHead;
        await PullRequestAsync();
        TimedRequest read = _h.Requests.First(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.True(read.At - _h.PushTimes[0] >= TimeSpan.FromSeconds(10));
        Assert.True(read.At - _h.PushTimes[0] < TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task ARedWatchWithNoThreadIsUnsettledWithNoFeedbackRun()
    {
        _h.CheckRuns = _ => SettleHarness.RunsJson("build", "completed", "failure");
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Unsettled, result.End);
        Assert.False(result.Last!.IsGreen);
        Assert.Contains("checks: red, unresolved threads: 0", Out);
        Assert.Single(_h.CloneDirectories);
    }

    [Fact]
    public async Task ARedWatchWithOneThreadRunsFeedbackAndWritesTheSecondWatch()
    {
        _h.CheckRuns = _ => SettleHarness.RunsJson("build", "completed", "failure");
        _h.Threads = "always";
        SettleResult result = await IssueAsync(rounds: 1);
        Assert.Equal(SettleEnd.Unsettled, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(2, _h.CloneDirectories.Count);
        Assert.Equal(2, Watches);
        Assert.Equal("checks: red, unresolved threads: 1", Assert.Single(Out, l => l.StartsWith("checks:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ADisputedThreadIsResolvedAndTheSecondWatchSettles()
    {
        _h.Threads = "resolvable";
        _h.Report("disputed");
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Settled, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(2, _h.CloneDirectories.Count);
        int reply = _h.Requests.FindIndex(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal)
            && r.Content!.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal)
            && r.Content.Contains(SettleHarness.ThreadA, StringComparison.Ordinal));
        int resolve = _h.Requests.FindIndex(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal)
            && r.Content!.Contains("resolveReviewThread", StringComparison.Ordinal)
            && r.Content.Contains(SettleHarness.ThreadA, StringComparison.Ordinal));
        Assert.True(reply >= 0);
        Assert.True(resolve > reply);
        Assert.Equal("checks: green, unresolved threads: 0", Assert.Single(Out, l => l.StartsWith("checks:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AResolvingRoundSettlesAndRemovesEachWorkTree()
    {
        _h.Threads = "until-resolved";
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Settled, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(1, FeedbackRuns);
        Assert.Equal(["watch: start", "feedback: start", "watch: start"], Out.Where(l => l is "watch: start" or "feedback: start"));
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task TheRoundBudgetBoundsTheFeedbackRuns(int rounds)
    {
        _h.Threads = "always";
        SettleResult result = await IssueAsync(rounds: rounds);
        Assert.Equal(SettleEnd.Unsettled, result.End);
        Assert.Equal(rounds, result.Rounds);
        Assert.Equal(rounds, FeedbackRuns);
        Assert.Single(result.Last!.Threads);
    }

    [Fact]
    public async Task ARoundThatPushedNothingEndsTheLoopWithTheLastWatch()
    {
        _h.Threads = "watch-only";
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Unsettled, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(1, Watches);
        Assert.Single(result.Last!.Threads);
        Assert.Equal(0, FeedbackRuns);
        Assert.Equal("checks: green, unresolved threads: 1", Assert.Single(Out, l => l.StartsWith("checks:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARoundThatStopsEndsStoppedWithNoWatchAfterIt()
    {
        _h.Threads = "always";
        foreach (int build in new[] { 2, 3, 4, 5 })
        {
            _h.RedBuilds.Add(build);
        }

        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Stopped, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(1, Watches);
        Assert.NotNull(result.Last);
        Assert.Single(_h.Posts("/issues/10/comments"));
        Assert.DoesNotContain(Out, l => l.StartsWith("checks:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReviewRoundOnADeclinedClosingIssueEndsDeclinedWithNoSecondWatch()
    {
        _h.CheckRuns = _ => SettleHarness.RunsJson("build", "completed", "failure");
        _h.Threads = "always";
        _h.PullBody = "Closes #2";
        _h.ClosingLabels = ["cleanup", "refactor"];
        SettleResult result = await IssueAsync();
        Assert.Equal(SettleEnd.Declined, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(1, Watches);
        Assert.Contains("The labels refactor and cleanup exclude each other", Assert.Single(_h.Posts("/issues/10/comments")).Content, StringComparison.Ordinal);
        Assert.Single(_h.CloneDirectories);
        Assert.DoesNotContain(Out, l => l.StartsWith("checks:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AJobWithThreeRoundsWritesOneFactorySectionAndThreeFeedbackSectionsInOrder()
    {
        string path = Path.Combine(Path.GetTempPath(), $"summary-{Guid.NewGuid():N}.md");
        _h.Environment["GITHUB_STEP_SUMMARY"] = path;
        _h.Threads = "always";
        try
        {
            SettleResult result = await _h.SettleIssueAsync(SettleHarness.Options(), 12, Token);
            Assert.Equal(3, result.Rounds);
            Assert.Equal(4, _h.CloneDirectories.Count);
            Assert.Equal(
                ["## Factory run", "## Feedback run", "## Feedback run", "## Feedback run"],
                File.ReadAllLines(path).Where(line => line.StartsWith("## ", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task APullRequestWithNoThreadSettlesWithNoClone()
    {
        SettleResult result = await PullRequestAsync();
        Assert.Equal(SettleEnd.Settled, result.End);
        Assert.Equal(1, result.Rounds);
        Assert.Empty(_h.CloneDirectories);
        Assert.Equal(["feedback: start", "watch: start", "checks: green, unresolved threads: 0"], Out);
    }

    [Fact]
    public async Task APullRequestRoundWithTheBudgetSpentPostsOneCommentAndStillWatches()
    {
        _h.Threads = "always";
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            _h.RedBuilds.Add(build);
        }

        SettleResult result = await PullRequestAsync();
        Assert.Equal(SettleEnd.Unsettled, result.End);
        Assert.Single(_h.Posts("/issues/10/comments"));
        Assert.Equal(1, Watches);
    }

    [Fact]
    public async Task APullRequestRoundRunsTheBuildAndTheReviewAndWritesACommandLineForEachGateCommand()
    {
        _h.Threads = "always";
        _h.RedBuilds.Add(1);
        await PullRequestAsync(timeoutMinutes: 0);
        Assert.NotEmpty(_h.Builder.Of(Phase.Build));
        Assert.NotEmpty(_h.Reviewer.Of(Phase.Review));
        Assert.Contains(SettleHarness.Lines(_h.Error), l => l.StartsWith("command: dotnet test --no-restore", StringComparison.Ordinal) && l.EndsWith(" exited 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APullRequestRoundThatStopsCountsTheOpeningRunAndTheRound()
    {
        _h.Threads = "always";
        foreach (int build in new[] { 2, 3, 4, 5 })
        {
            _h.RedBuilds.Add(build);
        }

        SettleResult result = await _h.SettlePullRequestAsync(SettleHarness.Options(1), Token);
        Assert.Equal(SettleEnd.Stopped, result.End);
        Assert.Equal(2, result.Rounds);
        Assert.Equal(1, Watches);
    }

    [Fact]
    public async Task APullRequestWithATimedOutWatchIsUnsettled()
    {
        _h.CheckRuns = _ => """{"check_runs":[]}""";
        SettleResult result = await PullRequestAsync(timeoutMinutes: 0);
        Assert.Equal(SettleEnd.Unsettled, result.End);
        Assert.Contains("checks: red, unresolved threads: 0", Out);
    }

    [Fact]
    public async Task APullRequestOnADeclinedIssueEndsDeclinedWithOneCommentAndNoClone()
    {
        _h.Threads = "always";
        _h.PullBody = "Closes #1";
        _h.Labels.AddRange(["cleanup", "refactor"]);
        SettleResult result = await PullRequestAsync();
        Assert.Equal(SettleEnd.Declined, result.End);
        Assert.Null(result.Last);
        Assert.Equal(1, result.Rounds);
        Assert.Contains("The labels refactor and cleanup exclude each other", Assert.Single(_h.Posts("/issues/10/comments")).Content, StringComparison.Ordinal);
        Assert.Empty(_h.CloneDirectories);
        Assert.DoesNotContain(Out, l => l.StartsWith("checks:", StringComparison.Ordinal));
    }

    private const string CleanupBody = "Tidy.\n\n## Scope\n\nsrc/\n\n## Check\n\n```\ntrue\n```\n";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AFailedCheckWritesItsExitCodeToTheLog(int code)
    {
        _h.Labels.Add("cleanup");
        _h.IssueBody = CleanupBody;
        _h.CheckResults[0] = (code, "", "");
        await IssueAsync(timeoutMinutes: 0);
        Assert.Contains($"command: check exited {code}", SettleHarness.Lines(_h.Error));
    }

    [Fact]
    public async Task AFailedWorktreeAddThrowsAndWritesNoCheckLine()
    {
        _h.Labels.Add("cleanup");
        _h.IssueBody = CleanupBody;
        _h.WorktreeAddExit = 128;
        await Assert.ThrowsAsync<SandboxException>(() => IssueAsync(timeoutMinutes: 0));
        Assert.DoesNotContain(SettleHarness.Lines(_h.Error), l => l.StartsWith("command: check", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReviewOfTheHeadCommitByTheReviewerSendsNoRequest()
    {
        Assert.Equal(SettleEnd.Settled, (await IssueAsync(copilotReview: true)).End);
        Assert.Empty(ReviewRequests);
    }

    [Fact]
    public async Task AReviewOfAnEarlierCommitSendsOneRequest()
    {
        _h.Reviews = SettleHarness.ReviewsJson("copilot-pull-request-reviewer", OldHead);
        await IssueAsync(copilotReview: true, timeoutMinutes: 0);
        Assert.Single(ReviewRequests);
    }

    [Fact]
    public async Task AReviewOfTheHeadCommitByAnotherLoginSendsOneRequest()
    {
        _h.Reviews = SettleHarness.ReviewsJson("someone");
        await IssueAsync(copilotReview: true, timeoutMinutes: 0);
        Assert.Single(ReviewRequests);
    }

    [Fact]
    public async Task AFailedReviewRequestThrowsBeforeAnyCheckRunRead()
    {
        _h.Reviews = SettleHarness.NoReviewsJson();
        _h.ReviewRequestError = "not available";
        GitHubGraphQLException refused = await Assert.ThrowsAsync<GitHubGraphQLException>(() => IssueAsync(copilotReview: true));
        Assert.Contains("not available", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APullRequestWatchWithNoReviewSendsTheRequestBeforeTheCheckRuns()
    {
        _h.Reviews = SettleHarness.NoReviewsJson();
        await _h.SettlePullRequestAsync(SettleHarness.Options(0, true, 0), Token);
        int request = _h.Requests.FindIndex(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));
        Assert.True(request >= 0);
        Assert.True(request < _h.Requests.FindIndex(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheSecondWatchAfterAFeedbackPassRequestsTheReviewAgain()
    {
        _h.Reviews = SettleHarness.NoReviewsJson();
        _h.Threads = "always";
        await IssueAsync(rounds: 1, copilotReview: true, timeoutMinutes: 0);
        Assert.Equal(2, ReviewRequests.Count());
        Assert.Equal(2, _h.Requests.Count(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal)));
    }
}
