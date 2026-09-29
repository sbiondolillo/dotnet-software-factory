using System.Text.Json;
using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class ReviewThreadsTests
{
    private const string Errors = "{\"data\":null,\"errors\":[{\"message\":\"first bad\"},{\"message\":\"second bad\"}]}";

    private static (ReviewThreads Sut, RecordingHandler Handler) Make(int status, string body)
    {
        var handler = new RecordingHandler();
        handler.Answer(status, body);
        return (new ReviewThreads(new GitHubClient(new HttpClient(handler), "t")), handler);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReviewReadWithErrorsAndNullPullRequestThrowsGraphQl()
    {
        (ReviewThreads sut, _) = Make(200, "{\"data\":{\"repository\":{\"pullRequest\":null}},\"errors\":[{\"message\":\"nope\"}]}");
        var ex = await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadReviewsAsync("o", "r", 0, Token));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task AThreadReadWithErrorsThrowsGraphQl()
    {
        (ReviewThreads sut, _) = Make(200, Errors);
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadThreadsAsync("o", "r", 1, Token));
    }

    [Fact]
    public async Task AReplyWithErrorsThrowsGraphQl()
    {
        (ReviewThreads sut, _) = Make(200, Errors);
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReplyAsync("t", "b", Token));
    }

    [Fact]
    public async Task AResolveWithErrorsThrowsGraphQl()
    {
        (ReviewThreads sut, _) = Make(200, Errors);
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ResolveAsync("t", Token));
    }

    [Fact]
    public async Task SeveralErrorsAreAllInTheMessage()
    {
        (ReviewThreads sut, _) = Make(200, Errors);
        var ex = await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ResolveAsync("t", Token));
        Assert.Contains("first bad", ex.Message);
        Assert.Contains("second bad", ex.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(500)]
    public async Task AFailingStatusThrowsRequestExceptionOnEveryOperation(int status)
    {
        (ReviewThreads sut, _) = Make(status, "x");
        Assert.Equal(status, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadReviewsAsync("o", "r", 1, Token))).StatusCode);
        Assert.Equal(status, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadThreadsAsync("o", "r", 1, Token))).StatusCode);
        Assert.Equal(status, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReplyAsync("t", "b", Token))).StatusCode);
        Assert.Equal(status, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ResolveAsync("t", Token))).StatusCode);
    }

    [Fact]
    public async Task AReplyBodyWithAQuoteAndLineFeedRoundTrips()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make(200, "{\"data\":{}}");
        const string body = "say \"hi\"\nagain";
        await sut.ReplyAsync("t", body, Token);
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        Assert.Equal(body, document.RootElement.GetProperty("variables").GetProperty("body").GetString());
    }

    [Fact]
    public async Task ANullLineAndANullAuthorRead()
    {
        (ReviewThreads sut, _) = Make(200, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[{\"id\":\"i\",\"isResolved\":false,\"path\":\"p\",\"line\":null,\"comments\":{\"nodes\":[{\"author\":null,\"body\":\"b\"}]}}]}}}}}");
        ReviewThread thread = Assert.Single(await sut.ReadThreadsAsync("o", "r", 1, Token));
        Assert.Null(thread.Line);
        Assert.Equal("", Assert.Single(thread.Comments).Author);
    }

    [Fact]
    public async Task EmptyListsReadAsEmpty()
    {
        (ReviewThreads sut, _) = Make(200, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviews\":{\"nodes\":[]},\"reviewThreads\":{\"nodes\":[]}}}}}");
        Assert.Empty(await sut.ReadReviewsAsync("o", "r", 1, Token));
        Assert.Empty(await sut.ReadThreadsAsync("o", "r", 1, Token));
    }
}
