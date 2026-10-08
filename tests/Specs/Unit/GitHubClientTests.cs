using System.Text.Json;
using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class GitHubClientTests
{
    private static (GitHubClient Client, RecordingHandler Handler) Make(int status, string body)
    {
        var handler = new RecordingHandler();
        handler.Answer(status, body);
        return (new GitHubClient(new HttpClient(handler), "secret-token", TimeSpan.Zero), handler);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(199)]
    [InlineData(300)]
    public async Task NonSuccessThrowsWithMethodPathStatusAndBody(int status)
    {
        (GitHubClient client, _) = Make(status, "plain text");
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => client.SendAsync(HttpMethod.Post, "/x/y", "{}", TestContext.Current.CancellationToken));
        Assert.Equal(status, ex.StatusCode);
        Assert.Contains("POST", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/x/y", ex.Message, StringComparison.Ordinal);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
        Assert.Contains("plain text", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    public async Task SuccessDoesNotThrowEvenWithEmptyBody(int status)
    {
        (GitHubClient client, _) = Make(status, "");
        await client.SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadingAnIssueThrowsOnFailure()
    {
        (GitHubClient client, _) = Make(404, "nope");
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => client.ReadIssueAsync("o", "r", 1, TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task PostingACommentThrowsOnFailure()
    {
        (GitHubClient client, _) = Make(403, "nope");
        await Assert.ThrowsAsync<GitHubRequestException>(() => client.PostCommentAsync("o", "r", 1, "hi", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingBodyReadsAsEmpty()
    {
        (GitHubClient client, _) = Make(200, "{\"title\":\"t\",\"user\":{\"login\":\"u\"},\"labels\":[]}");
        GitHubIssue issue = await client.ReadIssueAsync("o", "r", 1, TestContext.Current.CancellationToken);
        Assert.Equal("", issue.Body);
    }

    [Fact]
    public async Task TokenIsInTheHeaderNotTheUri()
    {
        (GitHubClient client, RecordingHandler handler) = Make(200, "{}");
        await client.SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
        Assert.Equal("Bearer secret-token", handler.Requests[0].Headers["Authorization"]);
        Assert.DoesNotContain("secret-token", handler.Requests[0].Uri, StringComparison.Ordinal);
    }

    private static GitHubClient Client(RecordingHandler handler, TimeSpan? wait = null) =>
        new(new HttpClient(handler), "tok", wait ?? TimeSpan.Zero);

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(599)]
    [InlineData(429)]
    [InlineData(409)]
    public async Task TransientStatusIsRetriedThenSucceeds(int status)
    {
        var handler = new RecordingHandler();
        handler.Enqueue(status, "x");
        handler.Answer(200, "{\"a\":1}");
        JsonElement json = await Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
        Assert.Equal(1, json.GetProperty("a").GetInt32());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ForbiddenWithRetryAfterIsRetried()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(403, "x", 0);
        handler.Answer(200, "{}");
        await Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(300)]
    [InlineData(199)]
    public async Task OtherStatusThrowsAtOnce(int status)
    {
        var handler = new RecordingHandler();
        handler.Answer(status, "nope");
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken));
        Assert.Equal(status, ex.StatusCode);
        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EveryFailureThrowsTheLastAnswerAfterFourRequests()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(500, "first");
        handler.Enqueue(502, "second");
        handler.Enqueue(503, "third");
        handler.Answer(504, "last");
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken));
        Assert.Equal(504, ex.StatusCode);
        Assert.Contains("last", ex.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task SuccessOnTheLastAllowedRequestGivesBackTheJson()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(500, "x");
        handler.Enqueue(500, "x");
        handler.Enqueue(500, "x");
        handler.Answer(200, "{\"a\":2}");
        JsonElement json = await Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
        Assert.Equal(2, json.GetProperty("a").GetInt32());
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task HandlerExceptionIsNotRetried()
    {
        var handler = new RecordingHandler { Throw = new HttpRequestException("down") };
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryAfterOfZeroOverridesALargeBaseWait()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(500, "x", 0);
        handler.Answer(200, "{}");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Client(handler, TimeSpan.FromSeconds(30)).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RetryAfterOfTwoSecondsWaitsAboutTwoSeconds()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(429, "x", 1);
        handler.Answer(200, "{}");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Client(handler).SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task ARequestSentAgainCarriesTheSameValues()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(500, "x");
        handler.Answer(201, "{}");
        await Client(handler).SendAsync(HttpMethod.Post, "/x", "{\"t\":1}", TestContext.Current.CancellationToken);
        Assert.Equal(handler.Requests[0], handler.Requests[1] with { Headers = handler.Requests[0].Headers }, new RequestComparer());
        Assert.Equal("{\"t\":1}", handler.Requests[1].Content);
        Assert.Equal("Bearer tok", handler.Requests[1].Headers["Authorization"]);
        Assert.Equal(handler.Requests[0].Headers["User-Agent"], handler.Requests[1].Headers["User-Agent"]);
    }

    [Fact]
    public async Task ReadIssueAndPostCommentRetry()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(500, "x");
        handler.Answer(200, "{\"title\":\"t\",\"user\":{\"login\":\"u\"}}");
        GitHubIssue issue = await Client(handler).ReadIssueAsync("o", "r", 1, TestContext.Current.CancellationToken);
        Assert.Equal("t", issue.Title);
        Assert.Equal(2, handler.Requests.Count);

        var postHandler = new RecordingHandler();
        postHandler.Enqueue(500, "x");
        postHandler.Answer(201, "{}");
        await Client(postHandler).PostCommentAsync("o", "r", 1, "hi", TestContext.Current.CancellationToken);
        Assert.Equal(2, postHandler.Requests.Count);
    }

    private sealed class RequestComparer : IEqualityComparer<RecordedRequest>
    {
        public bool Equals(RecordedRequest? x, RecordedRequest? y) => x?.Method == y?.Method && x?.Uri == y?.Uri && x?.Content == y?.Content;

        public int GetHashCode(RecordedRequest obj) => obj.Uri.GetHashCode(StringComparison.Ordinal);
    }
}
