using System.Net;
using System.Text;
using Core;
using Moq;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class DeliverTests
{
    private static (Deliver Deliver, List<string> Git, RoutingHandler Handler) Build(string failing = "", int status = 200)
    {
        var git = new List<string>();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                git.Add(spec.Arguments[0]);
                return Task.FromResult(new CommandResult { ExitCode = spec.Arguments[0] == failing ? 1 : 0, StandardError = "fatal" });
            });
        var handler = new RoutingHandler { PostStatus = status, PostBody = "{\"number\":10,\"html_url\":\"https://x/pull/10\"}" };
        var client = new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero);
        return (new Deliver(new Git(mock.Object), new PullRequests(client), client), git, handler);
    }

    private static Task<PullRequest> RunAsync(Deliver deliver) =>
        deliver.RunAsync("/work", "abc", "o", "r", "main", 7, "Title", "token", TestContext.Current.CancellationToken);

    [Fact]
    public async Task APushThatFailsThrowsAndSendsNoRequest()
    {
        var (deliver, _, handler) = Build("push");
        await Assert.ThrowsAsync<GitCommandException>(() => RunAsync(deliver));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ACheckoutThatFailsThrowsAndRunsNoPushAndSendsNoRequest()
    {
        var (deliver, git, handler) = Build("checkout");
        await Assert.ThrowsAsync<GitCommandException>(() => RunAsync(deliver));
        Assert.Equal(["checkout"], git);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A422OnTheOpenThrowsAndPostsNoComment()
    {
        var (deliver, _, handler) = Build(status: 422);
        var exception = await Assert.ThrowsAsync<GitHubRequestException>(() => RunAsync(deliver));
        Assert.Equal(422, exception.StatusCode);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheHappyPathOpensOnePullRequestAndPostsOneComment()
    {
        var (deliver, git, handler) = Build();
        PullRequest result = await RunAsync(deliver);
        Assert.Equal(new PullRequest(10, "https://x/pull/10"), result);
        Assert.Equal(["checkout", "push"], git);
        Assert.Contains("\"body\":\"Closes #7\"", handler.Requests.Single(r => r.Uri.EndsWith("/pulls", StringComparison.Ordinal) && r.Method is "POST").Content);
        Assert.Contains("https://x/pull/10", Assert.Single(handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal)).Content);
    }
}
