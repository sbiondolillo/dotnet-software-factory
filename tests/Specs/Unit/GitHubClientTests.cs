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
        return (new GitHubClient(new HttpClient(handler), "secret-token"), handler);
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
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => client.SendAsync(HttpMethod.Post, "/x/y", "{}", TestContext.Current.CancellationToken));
        Assert.Equal(status, ex.StatusCode);
        Assert.Contains("POST", ex.Message);
        Assert.Contains("/x/y", ex.Message);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message);
        Assert.Contains("plain text", ex.Message);
        Assert.DoesNotContain("secret-token", ex.Message);
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
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => client.ReadIssueAsync("o", "r", 1, TestContext.Current.CancellationToken));
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
        Assert.DoesNotContain("secret-token", handler.Requests[0].Uri);
    }
}
