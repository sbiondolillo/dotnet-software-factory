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
    private readonly PullRequestHandler _handler = new();
    private PullRequests? _pullRequests;
    private PullRequest? _pullRequest;
    private IReadOnlyList<CheckRun>? _runs;

    public void Dispose() => _handler.Dispose();

    [Given("a PullRequests built over a GitHubClient with a stand-in HttpClient")]
    public void GivenBuilt() => _pullRequests = new PullRequests(new GitHubClient(new HttpClient(_handler), "token"));

    [Given("no pull request from the branch {string} is open")]
    public void GivenNoneOpen(string branch) => _handler.LookupBody = "[]";

    [Given("GitHub answers the open of a pull request with the number {int} and the html_url {string}")]
    public void GivenOpenAnswer(int number, string url) =>
        _handler.OpenBody = JsonSerializer.Serialize(new { number, html_url = url });

    [Given("the pull request {int} with the html_url {string} is already open from the branch {string}")]
    public void GivenAlreadyOpen(int number, string url, string branch) =>
        _handler.LookupBody = JsonSerializer.Serialize(new[] { new { number, html_url = url } });

    [Given("GitHub answers the read of check runs with one check run with the name {string}, the status {string} and the conclusion {string}")]
    public void GivenOneRun(string name, string status, string conclusion) =>
        _handler.CheckBody = JsonSerializer.Serialize(new { total_count = 1, check_runs = new[] { new { name, status, conclusion } } });

    [Given("GitHub answers the read of check runs with one check run with the name {string}, the status {string} and a null conclusion")]
    public void GivenNullConclusion(string name, string status) =>
        _handler.CheckBody = JsonSerializer.Serialize(new { total_count = 1, check_runs = new[] { new { name, status, conclusion = (string?)null } } });

    [Given("GitHub answers the read of check runs with the total_count 0 and an empty list of check runs")]
    public void GivenNoRuns() => _handler.CheckBody = "{\"total_count\":0,\"check_runs\":[]}";

    [Given("GitHub answers the read of check runs with the status code {int}")]
    public void GivenCheckStatus(int status) => _handler.CheckStatus = status;

    [When("I open a pull request on the repo {string} of the owner {string} from the branch {string} into {string} with the title {string} and the body {string}")]
    public async Task WhenOpen(string repo, string owner, string head, string baseBranch, string title, string body) =>
        _pullRequest = await _pullRequests!.OpenAsync(owner, repo, head, baseBranch, title, body);

    [When("I read the check runs on the commit {string} of the repo {string} of the owner {string}")]
    public async Task WhenRead(string sha, string repo, string owner)
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

    [Then("the content of the recorded open request is JSON whose {string} is {string}, whose {string} is {string}, whose {string} is {string} and whose {string} is {string}")]
    public void ThenOpenJson(string n1, string v1, string n2, string v2, string n3, string v3, string n4, string v4)
    {
        using JsonDocument document = JsonDocument.Parse(_handler.Requests.Single(r => r.Method is "POST").Content!);
        Assert.Equal(v1, document.RootElement.GetProperty(n1).GetString());
        Assert.Equal(v2, document.RootElement.GetProperty(n2).GetString());
        Assert.Equal(v3, document.RootElement.GetProperty(n3).GetString());
        Assert.Equal(v4, document.RootElement.GetProperty(n4).GetString());
    }

    [Then("the returned pull request has the number {int} and the web address {string}")]
    public void ThenReturned(int number, string url)
    {
        Assert.Equal(number, _pullRequest!.Number);
        Assert.Equal(url, _pullRequest.HtmlUrl);
    }

    [Then("the recorded check-runs request has the method {string} and the address {string}")]
    public void ThenCheckRequest(string method, string address)
    {
        RecordedRequest request = _handler.Requests.Single();
        Assert.Equal(method, request.Method);
        Assert.Equal(address, request.Uri);
    }

    [Then("PullRequests returns one check run")]
    public void ThenOne() => Assert.Single(_runs!);

    [Then("the check run has the name {string}, the status {string} and the conclusion {string}")]
    public void ThenRun(string name, string status, string conclusion) =>
        Assert.Equal(new CheckRun(name, status, conclusion), _runs![0]);

    [Then("the check run has the name {string} and the status {string}")]
    public void ThenNameStatus(string name, string status)
    {
        Assert.Equal(name, _runs![0].Name);
        Assert.Equal(status, _runs[0].Status);
    }

    [Then("the check run has the empty string as its conclusion")]
    public void ThenEmptyConclusion() => Assert.Equal("", _runs![0].Conclusion);

    [Then("PullRequests returns an empty list of check runs")]
    public void ThenEmpty() => Assert.Empty(_runs!);

    [Then("PullRequests throws a GitHubRequestException that gives the status code {int}")]
    public void ThenThrows(int status) =>
        Assert.Equal(status, Assert.IsType<GitHubRequestException>(exceptionState.Exception).StatusCode);
}

public sealed class PullRequestHandler : HttpMessageHandler
{
    public string LookupBody { get; set; } = "[]";

    public string OpenBody { get; set; } = "{\"number\":1,\"html_url\":\"u\"}";

    public string CheckBody { get; set; } = "{\"total_count\":0,\"check_runs\":[]}";

    public int CheckStatus { get; set; } = 200;

    public int LookupStatus { get; set; } = 200;

    public int OpenStatus { get; set; } = 201;

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, [], content));
        string path = request.RequestUri.AbsolutePath;
        (int status, string body) = path.EndsWith("/check-runs", StringComparison.Ordinal) ? (CheckStatus, CheckBody)
            : request.Method == HttpMethod.Post ? (OpenStatus, OpenBody)
            : (LookupStatus, LookupBody);
        return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
