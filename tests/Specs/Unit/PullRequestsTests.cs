using System.Text.Json;
using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class PullRequestsTests
{
    private const string Old = "d7d0afd495cc64aa2ac153de8d001ad8d86fe074";

    private static (PullRequests Sut, RoutingHandler Handler) Make()
    {
        var handler = new RoutingHandler();
        return (new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero)), handler);
    }

    [Theory]
    [InlineData(422)]
    [InlineData(404)]
    public async Task ACheckRunReadThatFailsThrowsWithTheStatus(int status)
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.ReadStatus = status;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadCheckRunsAsync(new RepoName("o", "r"), "abc", TestContext.Current.CancellationToken));
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task ALookupThatFailsThrowsAndSendsNoPost()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.LookupStatus = 500;
        await Assert.ThrowsAsync<GitHubRequestException>(() => sut.OpenAsync(new RepoName("o", "r"), "b", "main", "t", "x", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requests, r => r.Method is "POST");
    }

    [Fact]
    public async Task APostThatFailsThrowsWithTheStatus()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.PostStatus = 422;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => sut.OpenAsync(new RepoName("o", "r"), "b", "main", "t", "x", TestContext.Current.CancellationToken));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task ANullConclusionReadsAsEmptyAndAStringConclusionIsKept()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.ReadBody = "{\"check_runs\":[{\"name\":\"a\",\"status\":\"queued\",\"conclusion\":null},{\"name\":\"b\",\"status\":\"completed\",\"conclusion\":\"failure\"}]}";
        IReadOnlyList<CheckRun> runs = await sut.ReadCheckRunsAsync(new RepoName("o", "r"), "abc", TestContext.Current.CancellationToken);
        Assert.Equal([new CheckRun("a", "queued", ""), new CheckRun("b", "completed", "failure")], runs);
    }

    [Fact]
    public async Task NoCheckRunsGivesAnEmptyList()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.ReadBody = "{\"total_count\": 0, \"check_runs\": []}";
        Assert.Empty(await sut.ReadCheckRunsAsync(new RepoName("o", "r"), "abc", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnOpenPullRequestFoundByTheLookupSendsNoPost()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.LookupBody = "[{\"number\":7,\"html_url\":\"u\"}]";
        PullRequest pr = await sut.OpenAsync(new RepoName("o", "r"), "b", "main", "t", "x", TestContext.Current.CancellationToken);
        Assert.Equal(new PullRequest(7, "u"), pr);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ATitleAndBodyWithQuotesAndNewlinesReachGitHubAsValidJson()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.PostBody = "{\"number\":1,\"html_url\":\"u\"}";
        await sut.OpenAsync(new RepoName("o", "r"), "b", "main", "say \"hi\"\nnow", "line1\n\"two\"", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(handler.Requests.Single(r => r.Method is "POST").Content!);
        Assert.Equal("say \"hi\"\nnow", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("line1\n\"two\"", doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task TheLookupCarriesTheEscapedHeadAndTheOpenState()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.PostBody = "{\"number\":1,\"html_url\":\"u\"}";
        await sut.OpenAsync(new RepoName("o", "r"), "factory/run-1", "main", "t", "x", TestContext.Current.CancellationToken);
        Assert.Equal("https://api.github.com/repos/o/r/pulls?head=o%3Afactory%2Frun-1&state=open", handler.Requests[0].Uri);
    }

    [Fact]
    public async Task ACheckRunReadAnswered500Then200SucceedsAfterTwoRequests()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(500, "x");
        handler.Answer(200, "{\"check_runs\":[]}");
        var sut = new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero));
        IReadOnlyList<CheckRun> runs = await sut.ReadCheckRunsAsync(new RepoName("o", "r"), "abc", TestContext.Current.CancellationToken);
        Assert.Empty(runs);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AHeadShaReadThatFailsThrowsWith404()
    {
        (PullRequests sut, RoutingHandler handler) = Make();
        handler.LookupStatus = 404;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadHeadShaAsync(new IssueNumber(new RepoName("o", "r"), 1), TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }

    private static (PullRequests Sut, WatchHandler Handler, FakeTime Time) MakeHeadWait()
    {
        var handler = new WatchHandler();
        var time = new FakeTime();
        return (new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero), null, time.WaitAsync, time), handler, time);
    }

    [Fact]
    public async Task TheHeadWaitGivesThePushedHeadWhenGitHubReadsItWithinTheGrace()
    {
        (PullRequests sut, WatchHandler handler, FakeTime time) = MakeHeadWait();
        foreach (int read in new[] { 1, 2, 3 })
        {
            handler.ShaAt(read, Old);
        }

        string head = await sut.ReadHeadShaAsync(new IssueNumber(new RepoName("o", "r"), 1), handler.Sha, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(handler.Sha, head);
        Assert.Equal(4, handler.PullReads);
        Assert.Equal(TimeSpan.FromSeconds(3), time.Now);
    }

    [Fact]
    public async Task TheHeadWaitGivesTheLastHeadItReadWhenTheGracePasses()
    {
        (PullRequests sut, WatchHandler handler, FakeTime time) = MakeHeadWait();
        handler.Sha = Old;
        string head = await sut.ReadHeadShaAsync(new IssueNumber(new RepoName("o", "r"), 1), "2878cae3e3bab7f3b1dcdfa14206f2c361021085", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(Old, head);
        Assert.Equal(TimeSpan.FromSeconds(10), time.Now);
        Assert.Equal(11, handler.PullReads);
    }

    [Fact]
    public async Task TheHeadWaitForAHeadThatGitHubReadsAtOnceReadsOnceAndWaitsForNothing()
    {
        (PullRequests sut, WatchHandler handler, FakeTime time) = MakeHeadWait();
        string head = await sut.ReadHeadShaAsync(new IssueNumber(new RepoName("o", "r"), 1), handler.Sha, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(handler.Sha, head);
        Assert.Equal(1, handler.PullReads);
        Assert.Equal(TimeSpan.Zero, time.Now);
    }
}
