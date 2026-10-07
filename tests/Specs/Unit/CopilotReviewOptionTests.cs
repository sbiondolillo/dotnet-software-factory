using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class CopilotReviewOptionTests : IDisposable
{
    private readonly ConsoleHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData("run --repo a/b --issue 1 --copilot-review maybe")]
    [InlineData("feedback --repo a/b --pr 1 --copilot-review maybe")]
    [InlineData("run --repo a/b --issue 1 --copilot-review ON")]
    [InlineData("feedback --repo a/b --pr 1 --copilot-review ON")]
    public async Task AValueThatIsNeitherOnNorOffIsRefusedWithOneLine(string arguments)
    {
        int exit = await _h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);
        Assert.Equal(1, exit);
        Assert.Contains("--copilot-review", Assert.Single(ConsoleHarness.Lines(_h.Error)), StringComparison.Ordinal);
        Assert.Empty(_h.Requests);
    }

    [Theory]
    [InlineData("completed", "success", true)]
    [InlineData("completed", "skipped", true)]
    [InlineData("completed", "failure", false)]
    public async Task WithNoReviewRequiredTheWatchSettlesOnTheChecksAlone(string status, string conclusion, bool green)
    {
        (PullRequestWatch sut, WatchHandler handler) = Make(WatchHandler.RunsJson([("build", status, conclusion)]));
        PullRequestWatchResult result = await sut.WatchAsync("o", "r", 1, null, TimeSpan.Zero, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.Equal(green, result.IsGreen);
        Assert.Equal(0, handler.ReviewReads);
    }

    [Fact]
    public async Task WithNoReviewRequiredARunInProgressIsWaitedFor()
    {
        (PullRequestWatch sut, WatchHandler handler) = Make(WatchHandler.RunsJson([("build", "completed", "success")]));
        handler.CheckRunsAt(1, WatchHandler.RunsJson([("build", "in_progress", null)]));
        PullRequestWatchResult result = await sut.WatchAsync("o", "r", 1, null, TimeSpan.Zero, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);
        Assert.True(result.IsGreen);
    }

    [Fact]
    public async Task WithNoReviewRequiredNoCheckRunIsRedAfterTheTimeout()
    {
        (PullRequestWatch sut, _) = Make(WatchHandler.RunsJson([]));
        PullRequestWatchResult result = await sut.WatchAsync("o", "r", 1, null, TimeSpan.Zero, TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.False(result.IsGreen);
    }

    [Theory]
    [InlineData("run --repo a/b --issue 1 --copilot-review off")]
    [InlineData("feedback --repo a/b --pr 10 --copilot-review off")]
    public async Task WithTheReviewOffNoReviewRequestGoesToGitHub(string arguments)
    {
        _h.Reviews = ConsoleHarness.NoReviewsJson();
        await _h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));
    }

    private static (PullRequestWatch Sut, WatchHandler Handler) Make(string runs)
    {
        var handler = new WatchHandler { CheckRunsBody = runs, ReviewsBody = WatchHandler.ReviewsJson([]) };
        var client = new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero);
        return (new PullRequestWatch(new PullRequests(client), new ReviewThreads(client)), handler);
    }
}
