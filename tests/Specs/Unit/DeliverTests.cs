using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class DeliverTests
{
    private static Task<PullRequest> Run(DeliverHarness harness) =>
        harness.Make().DeliverAsync("/work", "a1b2c3d4", "o", "r", "main", 12, "T", "token", TestContext.Current.CancellationToken);

    [Fact]
    public async Task AFailedCheckoutThrowsAndRunsNoPushAndSendsNoRequest()
    {
        using var harness = new DeliverHarness { FailingGitSubcommand = "checkout" };
        await Assert.ThrowsAsync<GitCommandException>(() => Run(harness));
        Assert.DoesNotContain(harness.Commands, c => c.Arguments[0] is "push");
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task ARejectedOpenLeavesTheBranchPushedAndPostsNoComment()
    {
        using var harness = new DeliverHarness();
        harness.Handler.PullStatus = 422;
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Run(harness));
        Assert.Equal(422, ex.StatusCode);
        Assert.Single(harness.Commands, c => c.Arguments[0] is "push");
        Assert.DoesNotContain(harness.Handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedCommentThrowsAfterThePullRequestIsOpened()
    {
        using var harness = new DeliverHarness();
        harness.Handler.CommentStatus = 403;
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Run(harness));
        Assert.Equal(403, ex.StatusCode);
        Assert.Contains(harness.Handler.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheCommandsRunInOrderAndOnlyThePushCarriesTheToken()
    {
        using var harness = new DeliverHarness();
        await Run(harness);
        Assert.Equal(2, harness.Commands.Count);
        Assert.Equal(["checkout", "-b", "factory/a1b2c3d4"], harness.Commands[0].Arguments);
        Assert.Equal(["push", "origin", "factory/a1b2c3d4"], harness.Commands[1].Arguments);
        Assert.All(harness.Commands, c => Assert.Equal("/work", c.WorkingDirectory));
        Assert.Empty(harness.Commands[0].EnvironmentVariables);
        Assert.Equal("token", harness.Commands[1].EnvironmentVariables["FACTORY_GITHUB_TOKEN"]);
        Assert.All(harness.Commands, c => Assert.DoesNotContain("token", c.Arguments));
    }
}
