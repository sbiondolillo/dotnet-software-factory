using System.Text.Json;
using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class ReviewThreadsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ReviewThreads Sut, RecordingHandler Handler) Make(int status, string body)
    {
        var handler = new RecordingHandler();
        handler.Answer(status, body);
        return (new ReviewThreads(new GitHubClient(new HttpClient(handler), "t")), handler);
    }

    private const string TwoErrors = "{\"errors\":[{\"message\":\"first bad\"},{\"message\":\"second bad\"}]}";

    [Fact]
    public async Task ErrorsOnTheReadOfReviewsThrow()
    {
        (ReviewThreads sut, _) = Make(200, TwoErrors);
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadReviewsAsync("o", "r", 1, Ct));
    }

    [Fact]
    public async Task ErrorsOnTheReplyThrow()
    {
        (ReviewThreads sut, _) = Make(200, TwoErrors);
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReplyAsync("PRRT_unknown", "x", Ct));
    }

    [Fact]
    public async Task ErrorsOnTheResolveThrow()
    {
        (ReviewThreads sut, _) = Make(200, TwoErrors);
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ResolveAsync("PRRT_unknown", Ct));
    }

    [Fact]
    public async Task ASingleErrorMessageIsHeld()
    {
        (ReviewThreads sut, _) = Make(200, "{\"errors\":[{\"message\":\"only bad\"}]}");
        var ex = await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ResolveAsync("x", Ct));
        Assert.Contains("only bad", ex.Message);
    }

    [Fact]
    public async Task ErrorsWithNullPullRequestThrowGraphQL()
    {
        (ReviewThreads sut, _) = Make(200, "{\"data\":{\"repository\":{\"pullRequest\":null}},\"errors\":[{\"message\":\"nope\"}]}");
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadThreadsAsync("o", "r", 0, Ct));
    }

    [Fact]
    public async Task A401OnEveryOperationThrowsWithTheStatus()
    {
        (ReviewThreads sut, _) = Make(401, "{}");
        Assert.Equal(401, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadThreadsAsync("o", "r", 1, Ct))).StatusCode);
        Assert.Equal(401, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReplyAsync("t", "b", Ct))).StatusCode);
        Assert.Equal(401, (await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ResolveAsync("t", Ct))).StatusCode);
    }

    [Fact]
    public async Task TheReplyTextTravelsOnlyInVariables()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make(200, "{\"data\":{}}");
        const string Text = "say \"hi\"\nthere";
        await sut.ReplyAsync("PRRT_1", Text, Ct);
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        Assert.Equal(Text, document.RootElement.GetProperty("variables").GetProperty("body").GetString());
        Assert.DoesNotContain("say", document.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public async Task TheNumberVariableIsAJsonNumber()
    {
        (ReviewThreads sut, RecordingHandler handler) = Make(200, "{\"data\":{}}");
        await sut.ReadReviewsAsync("o", "r", 30, Ct);
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("variables").GetProperty("number").ValueKind);
    }

    [Fact]
    public async Task ANullCommentAuthorReadsAsEmpty()
    {
        (ReviewThreads sut, _) = Make(200, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[{\"id\":\"i\",\"isResolved\":false,\"path\":\"p\",\"line\":2,\"comments\":{\"nodes\":[{\"author\":null,\"body\":\"b\"}]}}]}}}}}");
        ReviewThread thread = Assert.Single(await sut.ReadThreadsAsync("o", "r", 1, Ct));
        Assert.Equal(new ReviewComment("", "b"), Assert.Single(thread.Comments));
    }
}
