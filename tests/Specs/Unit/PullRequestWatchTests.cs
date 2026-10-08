using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class PullRequestWatchTests
{
    private static readonly (string, string, string?)[] Done = [("build", "completed", "success")];

    private static (PullRequestWatch Sut, WatchHandler Handler) Make()
    {
        var handler = new WatchHandler
        {
            CheckRunsBody = WatchHandler.RunsJson(Done),
            ReviewsBody = WatchHandler.ReviewsJson([("me", "COMMENTED", new WatchHandler().Sha)]),
        };
        var client = new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero);
        return (new PullRequestWatch(new PullRequests(client), new ReviewThreads(client)), handler);
    }

    private static Task<PullRequestWatchResult> Watch(PullRequestWatch sut, TimeSpan timeout, TimeSpan? wait = null, CancellationToken token = default) =>
        sut.WatchAsync("o", "r", 1, "me", wait ?? TimeSpan.Zero, timeout, token);

    [Fact]
    public async Task APullRequestReadThatFailsThrowsWith404()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.PullStatus = 404;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(403)]
    public async Task ACheckRunReadThatFailsThrowsWithTheStatus(int status)
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckStatus = status;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken));
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task AReviewReadThatFailsThrows()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ReviewStatus = 403;
        await Assert.ThrowsAsync<GitHubRequestException>(() => Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AThreadReadThatFailsThrows()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ThreadStatus = 403;
        await Assert.ThrowsAsync<GitHubRequestException>(() => Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("in_progress", null)]
    [InlineData("queued", null)]
    public async Task AnUnfinishedCheckRunWithTimeoutZeroIsRedAfterOnePoll(string status, string? conclusion)
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([("build", status, conclusion)]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.Equal(1, handler.CheckRunReads);
    }

    [Fact]
    public async Task NoCheckRunWithTimeoutZeroIsRedAfterOnePoll()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.Equal(1, handler.CheckRunReads);
    }

    [Fact]
    public async Task NoReviewWithTimeoutZeroIsRedAndKeepsTheUnresolvedThreads()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ReviewsBody = WatchHandler.ReviewsJson([]);
        handler.ThreadsBody = WatchHandler.ThreadsJson([("T1", false, "a", 1, [])]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.Equal("T1", Assert.Single(result.Threads).Id);
    }

    [Fact]
    public async Task AReviewByAnotherLoginOrNoAuthorDoesNotSettle()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ReviewsBody = WatchHandler.ReviewsJson([("other", "COMMENTED", handler.Sha), (null, "COMMENTED", handler.Sha)]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
    }

    [Fact]
    public async Task AFailedCheckRunIsRedAtOnceWithNoSecondPoll()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([("build", "completed", "failure")]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.FromHours(1), null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.Equal(1, handler.CheckRunReads);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("timed_out")]
    [InlineData("neutral")]
    [InlineData("action_required")]
    public async Task AnyOtherConclusionIsRed(string conclusion)
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([("build", "completed", conclusion)]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.FromHours(1), null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
    }

    [Fact]
    public async Task ASkippedConclusionAloneIsGreen()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([("build", "completed", "skipped")]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.FromHours(1), null, TestContext.Current.CancellationToken);
        Assert.True(result.IsGreen);
    }

    [Fact]
    public async Task SettlingAtTheSecondPollIsGreenAfterTwoPolls()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsAt(1, WatchHandler.RunsJson([("build", "in_progress", null)]));
        PullRequestWatchResult result = await Watch(sut, TimeSpan.FromHours(1), null, TestContext.Current.CancellationToken);
        Assert.True(result.IsGreen);
        Assert.Equal(2, handler.CheckRunReads);
        Assert.Equal(2, handler.PullReads);
    }

    [Fact]
    public async Task ATimeoutWithAWaitEndsRed()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.True(handler.CheckRunReads > 1);
    }

    [Fact]
    public async Task CancellingDuringTheWaitThrowsAndPollsNoMore()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([]);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Watch(sut, TimeSpan.FromHours(1), TimeSpan.FromHours(1), source.Token));
        Assert.Equal(1, handler.CheckRunReads);
    }

    [Fact]
    public async Task CancellingBeforeTheStartThrows()
    {
        (PullRequestWatch sut, _) = Make();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Watch(sut, TimeSpan.Zero, null, source.Token));
    }

    [Fact]
    public async Task ResolvedThreadsAreDroppedAndUnresolvedKeptInOrder()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ThreadsBody = WatchHandler.ThreadsJson([("A", false, "a", 1, []), ("B", true, "a", 2, []), ("C", false, "a", 3, [])]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        Assert.Equal(["A", "C"], result.Threads.Select(t => t.Id));
    }

    [Fact]
    public async Task ASettledRedResultKeepsOnlyTheUnresolvedThreads()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.CheckRunsBody = WatchHandler.RunsJson([("build", "completed", "failure")]);
        handler.ThreadsBody = WatchHandler.ThreadsJson([("A", true, "a", 1, []), ("B", false, "a", 2, [])]);
        PullRequestWatchResult result = await Watch(sut, TimeSpan.FromHours(1), null, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.Equal("B", Assert.Single(result.Threads).Id);
    }

    [Fact]
    public async Task AReviewOfAnEarlierCommitDoesNotSettle()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ReviewsBody = WatchHandler.ReviewsJson([("me", "COMMENTED", "d7d0afd495cc64aa2ac153de8d001ad8d86fe074")]);
        Assert.False((await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken)).IsGreen);
    }

    [Fact]
    public async Task AReviewWithNoCommitDoesNotSettle()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make();
        handler.ReviewsBody = WatchHandler.ReviewsJson([("me", "PENDING", "")]);
        Assert.False((await Watch(sut, TimeSpan.Zero, null, TestContext.Current.CancellationToken)).IsGreen);
    }
}
