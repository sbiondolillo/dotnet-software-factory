using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class ReviewRequestOverRefusalTests : IDisposable
{
    private const string Refusal = "Copilot was unable to review this pull request because the user who requested the review has reached their quota limit.";
    private readonly ConsoleHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static (PullRequestWatch Sut, WatchHandler Handler) Make(params (string? Login, string State, string Commit)[] reviews)
    {
        var handler = new WatchHandler
        {
            CheckRunsBody = WatchHandler.RunsJson([("build", "completed", "success")]),
            ReviewsBody = WatchHandler.ReviewsJson(reviews),
        };
        var client = new GitHubClient(new HttpClient(handler), "token");
        return (new PullRequestWatch(new PullRequests(client), new ReviewThreads(client)), handler);
    }

    [Fact]
    public async Task ASkippedReviewDoesNotSettleTheWatch()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make(("me", "COMMENTED", new WatchHandler().Sha));
        PullRequestWatchResult result = await sut.WatchAsync("o", "r", 1, "me", TimeSpan.Zero, TimeSpan.Zero, 1, handler.Sha, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public async Task ANewReviewAfterTheSkippedOnesSettlesTheWatch()
    {
        string sha = new WatchHandler().Sha;
        (PullRequestWatch sut, WatchHandler handler) = Make(("me", "COMMENTED", sha), ("me", "COMMENTED", sha));
        PullRequestWatchResult result = await sut.WatchAsync("o", "r", 1, "me", TimeSpan.Zero, TimeSpan.Zero, 1, handler.Sha, TestContext.Current.CancellationToken);
        Assert.True(result.IsGreen);
    }

    [Fact]
    public async Task AReviewOnAnotherHeadIsNotSkipped()
    {
        string sha = new WatchHandler().Sha;
        (PullRequestWatch sut, _) = Make(("me", "COMMENTED", sha));
        PullRequestWatchResult result = await sut.WatchAsync("o", "r", 1, "me", TimeSpan.Zero, TimeSpan.Zero, 1, "other", TestContext.Current.CancellationToken);
        Assert.True(result.IsGreen);
    }

    [Theory]
    [InlineData("run --repo a/b --issue 1 --copilot-review on", 1)]
    [InlineData("feedback --repo a/b --pr 10 --copilot-review on", 1)]
    public async Task ARefusedHeadGetsOneReviewRequest(string arguments, int requests)
    {
        _h.Reviews = ConsoleHarness.ReviewsJson("copilot-pull-request-reviewer", ConsoleHarness.DefaultSha, Refusal);
        await _h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);
        Assert.Equal(requests, _h.Requests.Count(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AReviewedHeadThatIsNotRefusedGetsNoRequest()
    {
        _h.Reviews = ConsoleHarness.ReviewsJson("copilot-pull-request-reviewer", ConsoleHarness.DefaultSha, "<!-- ccr-overview-v2 -->");
        await _h.RunAsync("feedback --repo a/b --pr 10 --copilot-review on".Split(' '), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));
    }
}
