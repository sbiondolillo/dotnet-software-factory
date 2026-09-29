using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class PullRequestsSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly RoutingHandler _handler = new();
    private PullRequests? _pullRequests;
    private PullRequest? _pullRequest;
    private IReadOnlyList<CheckRun>? _runs;

    public void Dispose() => _handler.Dispose();

    [Given("a PullRequests built over a GitHub client")]
    public void GivenBuilt() => _pullRequests = new PullRequests(new GitHubClient(new HttpClient(_handler), "token"));

    [Given("no pull request from the branch {string} is open")]
    public void GivenNoneOpen(string branch) => _handler.LookupBody = "[]";

    [Given("GitHub answers the open of a pull request with the number {int} and the html_url {string}")]
    public void GivenOpenAnswer(int number, string url) =>
        _handler.PostBody = JsonSerializer.Serialize(new { number, html_url = url });

    [Given("the pull request {int}, with the html_url {string}, is already open from the branch {string}")]
    public void GivenAlreadyOpen(int number, string url, string branch) =>
        _handler.LookupBody = JsonSerializer.Serialize(new[] { new { number, html_url = url } });

    [Given("GitHub answers the read of check runs with no check runs")]
    public void GivenNoRuns() => _handler.ReadBody = "{\"total_count\": 0, \"check_runs\": []}";

    [Given("GitHub answers the read of check runs with one check run with the name {string}, the status {string} and the conclusion {string}")]
    public void GivenOneRun(string name, string status, string conclusion) =>
        _handler.ReadBody = JsonSerializer.Serialize(new { total_count = 1, check_runs = new[] { new { name, status, conclusion } } });

    [Given("GitHub answers the read of check runs with one check run with the name {string}, the status {string} and the conclusion null")]
    public void GivenOneRunNull(string name, string status) =>
        _handler.ReadBody = JsonSerializer.Serialize(new { total_count = 1, check_runs = new[] { new { name, status, conclusion = (string?)null } } });

    [Given("GitHub answers the read of check runs with the status code {int}")]
    public void GivenReadStatus(int status)
    {
        _handler.ReadStatus = status;
        _handler.ReadBody = "{\"message\":\"No commit found\"}";
    }

    [When("a caller opens a pull request on the repo {string} of the owner {string} from the branch {string} into {string} with the title {string} and the body {string}")]
    public async Task WhenOpens(string repo, string owner, string head, string baseBranch, string title, string body) =>
        _pullRequest = await _pullRequests!.OpenAsync(owner, repo, head, baseBranch, title, body);

    [When("a caller reads the check runs on the commit {string} of the repo {string} of the owner {string}")]
    public async Task WhenReads(string sha, string repo, string owner)
    {
        try
        {
            _runs = await _pullRequests!.ReadCheckRunsAsync(owner, repo, sha);
        }
        catch (GitHubRequestException ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("the recorded open request has the method {string} and the address {string}")]
    public void ThenOpenRequest(string method, string address)
    {
        RecordedRequest request = _handler.Requests.Single(r => r.Method is "POST");
        Assert.Equal(method, request.Method);
        Assert.Equal(address, request.Uri);
    }

    [Then("the content of the recorded open request is JSON whose head is {string}, whose base is {string}, whose title is {string} and whose body is {string}")]
    public void ThenOpenContent(string head, string baseBranch, string title, string body)
    {
        RecordedRequest request = _handler.Requests.Single(r => r.Method is "POST");
        using JsonDocument document = JsonDocument.Parse(request.Content!);
        JsonElement root = document.RootElement;
        Assert.Equal(head, root.GetProperty("head").GetString());
        Assert.Equal(baseBranch, root.GetProperty("base").GetString());
        Assert.Equal(title, root.GetProperty("title").GetString());
        Assert.Equal(body, root.GetProperty("body").GetString());
    }

    [Then("PullRequests returns a pull request with the number {int} and the web address {string}")]
    public void ThenReturns(int number, string url)
    {
        Assert.Equal(number, _pullRequest!.Number);
        Assert.Equal(url, _pullRequest.HtmlUrl);
    }

    [Then("the recorded read request has the method {string} and the address {string}")]
    public void ThenReadRequest(string method, string address)
    {
        RecordedRequest request = _handler.Requests.Single(r => r.Uri.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.Equal(method, request.Method);
        Assert.Equal(address, request.Uri);
    }

    [Then("PullRequests returns one check run with the name {string}, the status {string} and the conclusion {string}")]
    public void ThenOneRun(string name, string status, string conclusion) =>
        Assert.Equal(new CheckRun(name, status, conclusion), Assert.Single(_runs!));

    [Then("PullRequests returns one check run with the name {string}, the status {string} and the empty string as its conclusion")]
    public void ThenOneRunEmpty(string name, string status) =>
        Assert.Equal(new CheckRun(name, status, ""), Assert.Single(_runs!));

    [Then("PullRequests returns an empty list of check runs")]
    public void ThenEmpty() => Assert.Empty(_runs!);

    [Then("PullRequests throws a GitHubRequestException that gives the status code {int}")]
    public void ThenThrows(int status) =>
        Assert.Equal(status, Assert.IsType<GitHubRequestException>(exceptionState.Exception).StatusCode);
}

/// <summary>A stand-in for GitHub that answers the lookup, the open and the check-run read each on its own.</summary>
public sealed class RoutingHandler : HttpMessageHandler
{
    public string LookupBody { get; set; } = "[]";

    public int LookupStatus { get; set; } = 200;

    public string PostBody { get; set; } = "{}";

    public int PostStatus { get; set; } = 200;

    public string ReadBody { get; set; } = "{}";

    public int ReadStatus { get; set; } = 200;

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, [], content));
        (int status, string body) = request.RequestUri.AbsolutePath.EndsWith("/check-runs", StringComparison.Ordinal)
            ? (ReadStatus, ReadBody)
            : request.Method == HttpMethod.Post ? (PostStatus, PostBody) : (LookupStatus, LookupBody);
        return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
