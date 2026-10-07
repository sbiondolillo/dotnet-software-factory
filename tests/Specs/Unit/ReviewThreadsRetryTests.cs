using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class ReviewThreadsRetryTests
{
    private const string Fault = "Something went wrong while executing your query on X.";
    private const string Good = "{\"data\":{\"repository\":{\"pullRequest\":{\"reviews\":{\"nodes\":[]}}}}}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Errors(params string[] messages) =>
        "{\"data\":null,\"errors\":[" + string.Join(",", messages.Select(m => "{\"message\":\"" + m + "\"}")) + "]}";

    private static (ReviewThreads Sut, RecordingHandler Handler) Make(TimeSpan? wait = null)
    {
        var handler = new RecordingHandler();
        return (new ReviewThreads(new GitHubClient(new HttpClient(handler), "t", wait ?? TimeSpan.Zero)), handler);
    }

    private static Task Call(ReviewThreads sut, int op) => op switch
    {
        0 => sut.ReadReviewsAsync("o", "r", 1, Token),
        1 => sut.RequestReviewAsync("id", "bot", Token),
        2 => sut.ReplyAsync("t", "b", Token),
        3 => sut.ResolveAsync("t", Token),
        _ => sut.ReadThreadsAsync("o", "r", 1, Token),
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AFaultOnEveryRequestThrowsAfterFourRequests(int op)
    {
        (ReviewThreads sut, RecordingHandler handler) = Make();
        handler.Answer(200, Errors(Fault));
        var ex = await Assert.ThrowsAsync<GitHubGraphQLException>(() => Call(sut, op));
        Assert.Contains("Something went wrong while executing your query", ex.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AFaultThenAnotherMessageThrowsAfterTwoRequests(int op)
    {
        (ReviewThreads sut, RecordingHandler handler) = Make();
        handler.Enqueue(200, Errors(Fault));
        handler.Answer(200, Errors("nope"));
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => Call(sut, op));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AFaultNotAtTheStartIsNoFault()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make();
        handler.Answer(200, Errors("Oops: Something went wrong while executing your query"));
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadReviewsAsync("o", "r", 1, Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AFaultAfterAnotherMessageIsSentAgain()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make();
        handler.Enqueue(200, Errors("nope", Fault));
        handler.Answer(200, Good);
        Assert.Empty(await sut.ReadReviewsAsync("o", "r", 1, Token));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TwoFaultsThenAGoodAnswerGivesTheResultAfterThreeRequests()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make();
        handler.Enqueue(200, Errors(Fault));
        handler.Enqueue(200, Errors(Fault));
        handler.Answer(200, Good);
        Assert.Empty(await sut.ReadReviewsAsync("o", "r", 1, Token));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public void TheBaseWaitIsExposed()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(5), new GitHubClient(new HttpClient(), "t", TimeSpan.FromMilliseconds(5)).BaseWait);
        Assert.Equal(TimeSpan.FromSeconds(2), new GitHubClient(new HttpClient(), "t").BaseWait);
    }

    [Fact]
    public async Task TheWaitBetweenSendsIsTheBaseWait()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make(TimeSpan.FromMilliseconds(100));
        handler.Answer(200, Errors(Fault));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadReviewsAsync("o", "r", 1, Token));
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(290), watch.Elapsed.ToString());
    }
}
