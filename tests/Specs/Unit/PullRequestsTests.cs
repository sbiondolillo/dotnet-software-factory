using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class PullRequestsTests
{
    private static (PullRequests Prs, PullRequestHandler Handler) Make()
    {
        var handler = new PullRequestHandler();
        return (new PullRequests(new GitHubClient(new HttpClient(handler), "t")), handler);
    }

    [Fact]
    public async Task AFailedOpenThrowsWithItsStatus()
    {
        (PullRequests prs, PullRequestHandler h) = Make();
        h.OpenStatus = 422;
        h.OpenBody = "{}";
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => prs.OpenAsync("o", "r", "b", "main", "t", "x", TestContext.Current.CancellationToken));
        Assert.Equal(422, ex.StatusCode);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task AFailedLookupThrowsAndSendsNoPost(int status)
    {
        (PullRequests prs, PullRequestHandler h) = Make();
        h.LookupStatus = status;
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => prs.OpenAsync("o", "r", "b", "main", "t", "x", TestContext.Current.CancellationToken));
        Assert.Equal(status, ex.StatusCode);
        Assert.DoesNotContain(h.Requests, r => r.Method is "POST");
    }

    [Fact]
    public async Task AFailedCheckRunReadWith404Throws()
    {
        (PullRequests prs, PullRequestHandler h) = Make();
        h.CheckStatus = 404;
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => prs.ReadCheckRunsAsync("o", "r", "s", TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task AnAbsentConclusionReadsAsEmptyAndSeveralRunsKeepTheirOrder()
    {
        (PullRequests prs, PullRequestHandler h) = Make();
        h.CheckBody = "{\"total_count\":2,\"check_runs\":[{\"name\":\"a\",\"status\":\"queued\"},{\"name\":\"b\",\"status\":\"completed\",\"conclusion\":\"failure\"}]}";
        IReadOnlyList<CheckRun> runs = await prs.ReadCheckRunsAsync("o", "r", "s", TestContext.Current.CancellationToken);
        Assert.Equal([new CheckRun("a", "queued", ""), new CheckRun("b", "completed", "failure")], runs);
    }

    [Fact]
    public async Task SpecialTextIsSentAsValidJsonAndReadsBack()
    {
        (PullRequests prs, PullRequestHandler h) = Make();
        await prs.OpenAsync("o", "r", "b\"ranch", "main", "ti\"tle\nx é", "bo\ndy \\ ü", TestContext.Current.CancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(h.Requests.Single(r => r.Method is "POST").Content!);
        Assert.Equal("b\"ranch", doc.RootElement.GetProperty("head").GetString());
        Assert.Equal("ti\"tle\nx é", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("bo\ndy \\ ü", doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task AnAlreadyOpenPullRequestSendsNoPostAndTheLookupEscapesTheBranch()
    {
        (PullRequests prs, PullRequestHandler h) = Make();
        h.LookupBody = "[{\"number\":7,\"html_url\":\"u7\"}]";
        PullRequest pr = await prs.OpenAsync("sbiondolillo", "r", "factory/run-1", "main", "t", "x", TestContext.Current.CancellationToken);
        Assert.Equal(new PullRequest(7, "u7"), pr);
        RecordedRequest only = Assert.Single(h.Requests);
        Assert.Equal("GET", only.Method);
        Assert.Contains("head=sbiondolillo%3Afactory%2Frun-1", only.Uri);
    }
}
