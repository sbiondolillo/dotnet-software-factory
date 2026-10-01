using System.Text.Json;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class DeliverTests
{
    [Fact]
    public async Task AFailedPushThrowsAndSendsNothingToGitHub()
    {
        using var h = new DeliverHarness("push");
        await Assert.ThrowsAsync<GitCommandException>(h.RunAsync);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task AFailedCheckoutRunsNoPushAndSendsNothing()
    {
        using var h = new DeliverHarness("checkout");
        await Assert.ThrowsAsync<GitCommandException>(h.RunAsync);
        Assert.DoesNotContain(h.Commands, c => c.Arguments[0] == "push");
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task ARejectedOpenThrowsAndPostsNoComment()
    {
        using var h = new DeliverHarness(postStatus: 422);
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(h.RunAsync);
        Assert.Equal(422, ex.StatusCode);
        Assert.DoesNotContain(h.Requests, r => r.Path.EndsWith("/issues/12/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedCommentThrows()
    {
        using var h = new DeliverHarness(commentStatus: 403);
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(h.RunAsync);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task TheOpenCarriesTheClosingLineAndTheIssueTitle()
    {
        using var h = new DeliverHarness();
        await h.RunAsync();
        var post = h.Requests.Single(r => r.Method is "POST" && r.Path.EndsWith("/pulls", StringComparison.Ordinal));
        using JsonDocument doc = JsonDocument.Parse(post.Content!);
        Assert.Equal("Title", doc.RootElement.GetProperty("title").GetString());
        Assert.StartsWith("Closes #12\n", doc.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePushCarriesTheWorkTreeAndTheTokenOnlyByEnvironment()
    {
        using var h = new DeliverHarness();
        await h.RunAsync();
        CommandSpec push = h.Commands.Single(c => c.Arguments[0] == "push");
        Assert.Equal("/work", push.WorkingDirectory);
        Assert.Equal(["push", "origin", "factory/a1b2c3d4"], push.Arguments);
        Assert.Equal("tok", push.EnvironmentVariables["FACTORY_GITHUB_TOKEN"]);
        Assert.DoesNotContain("tok", push.Arguments);
    }
}
