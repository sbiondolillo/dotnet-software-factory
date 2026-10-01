using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class DeliverSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly DeliverHarness _harness = new();
    private PullRequest? _result;
    private string _workTree = "";
    private string _runId = "";
    private string _owner = "";
    private string _repo = "";
    private string _baseBranch = "";
    private string _token = "";
    private int _issue;
    private string _title = "";

    public void Dispose() => _harness.Dispose();

    [Given("the work tree {string}, the run id {string}, the repo {string} of the owner {string}, the base branch {string} and the token {string}")]
    public void GivenValues(string workTree, string runId, string repo, string owner, string baseBranch, string token)
    {
        _workTree = workTree;
        _runId = runId;
        _repo = repo;
        _owner = owner;
        _baseBranch = baseBranch;
        _token = token;
    }

    [Given("the issue {int} has the title {string}")]
    public void GivenIssue(int number, string title)
    {
        _issue = number;
        _title = title;
    }

    [Given("git answers each command with the exit code 0")]
    public void GivenGitOk() => _harness.FailingGitSubcommand = null;

    [Given("git answers the push with the exit code {int}")]
    public void GivenPushFails(int code)
    {
        _harness.FailingGitSubcommand = "push";
        _harness.FailingExitCode = code;
    }

    [Given("GitHub answers the open of the pull request with the number {int} and the web address {string}")]
    public void GivenOpen(int number, string url) =>
        _harness.Handler.PullBody = JsonSerializer.Serialize(new { number, html_url = url });

    [Given("GitHub answers the open of the pull request with the status code {int}")]
    public void GivenOpenStatus(int status) => _harness.Handler.PullStatus = status;

    [When("a caller delivers the run")]
    public async Task WhenDelivers()
    {
        try
        {
            _result = await _harness.Make().DeliverAsync(_workTree, _runId, _owner, _repo, _baseBranch, _issue, _title, _token);
        }
        catch (Exception ex) when (ex is GitCommandException or GitHubRequestException)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("git pushes the branch {string} from {string} to {string}")]
    public void ThenPushes(string branch, string directory, string remote)
    {
        CommandSpec push = Assert.Single(_harness.Commands, c => c.Arguments[0] is "push");
        Assert.Equal(["push", remote, branch], push.Arguments);
        Assert.Equal(directory, push.WorkingDirectory);
    }

    [Then("Deliver opens a pull request on {string} from the branch {string} into {string} with the title {string}")]
    public void ThenOpens(string repoPath, string head, string baseBranch, string title)
    {
        RecordedRequest request = _harness.Handler.Requests.Single(r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
        Assert.Contains($"/repos/{repoPath}/pulls", request.Uri, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(request.Content!);
        Assert.Equal(head, document.RootElement.GetProperty("head").GetString());
        Assert.Equal(baseBranch, document.RootElement.GetProperty("base").GetString());
        Assert.Equal(title, document.RootElement.GetProperty("title").GetString());
    }

    [Then("the body of the pull request starts with the line {string}")]
    public void ThenBody(string line)
    {
        RecordedRequest request = _harness.Handler.Requests.Single(r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(request.Content!);
        string body = document.RootElement.GetProperty("body").GetString()!;
        Assert.Equal(line, body.Split('\n')[0]);
    }

    [Then("Deliver posts one comment on the issue {int} of {string}")]
    public void ThenComment(int number, string repoPath)
    {
        RecordedRequest request = Assert.Single(_harness.Handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal));
        Assert.Equal("POST", request.Method);
        Assert.Contains($"/repos/{repoPath}/issues/{number}/comments", request.Uri, StringComparison.Ordinal);
    }

    [Then("the comment holds {string}")]
    public void ThenCommentHolds(string text)
    {
        RecordedRequest request = Assert.Single(_harness.Handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(request.Content!);
        Assert.Contains(text, document.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    [Then("Deliver gives back the pull request with the number {int} and the web address {string}")]
    public void ThenGivesBack(int number, string url)
    {
        Assert.Equal(number, _result!.Number);
        Assert.Equal(url, _result.HtmlUrl);
    }

    [Then("Deliver throws GitCommandException")]
    public void ThenGitThrows() => Assert.IsType<GitCommandException>(exceptionState.Exception);

    [Then("Deliver throws GitHubRequestException")]
    public void ThenGitHubThrows() => Assert.IsType<GitHubRequestException>(exceptionState.Exception);

    [Then("no request is sent to GitHub")]
    public void ThenNoRequest() => Assert.Empty(_harness.Handler.Requests);

    [Then("the exception has the status code {int}")]
    public void ThenStatus(int status) =>
        Assert.Equal(status, Assert.IsType<GitHubRequestException>(exceptionState.Exception).StatusCode);

    [Then("no comment is posted")]
    public void ThenNoComment() =>
        Assert.DoesNotContain(_harness.Handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal));
}

/// <summary>A mock git and a stub GitHub under a <see cref="Deliver"/>, shared by the steps and the unit tests.</summary>
public sealed class DeliverHarness : IDisposable
{
    public DeliverStubHandler Handler { get; } = new();

    public List<CommandSpec> Commands { get; } = [];

    public string? FailingGitSubcommand { get; set; }

    public int FailingExitCode { get; set; } = 1;

    public void Dispose() => Handler.Dispose();

    public Deliver Make()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                Commands.Add(spec);
                int code = spec.Arguments[0] == FailingGitSubcommand ? FailingExitCode : 0;
                return Task.FromResult(new CommandResult { ExitCode = code, StandardError = code is 0 ? "" : "boom" });
            });
        var client = new GitHubClient(new HttpClient(Handler), "token", TimeSpan.Zero);
        return new Deliver(new Git(mock.Object), new PullRequests(client), client);
    }
}

/// <summary>A stand-in for GitHub that answers the lookup, the open of a pull request and the comment each on its own.</summary>
public sealed class DeliverStubHandler : HttpMessageHandler
{
    public string PullBody { get; set; } = "{\"number\":10,\"html_url\":\"https://github.com/o/r/pull/10\"}";

    public int PullStatus { get; set; } = 201;

    public int CommentStatus { get; set; } = 201;

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, [], content));
        string path = request.RequestUri.AbsolutePath;
        (int status, string body) = path.EndsWith("/comments", StringComparison.Ordinal)
            ? (CommentStatus, "{}")
            : request.Method == HttpMethod.Post ? (PullStatus, PullStatus is 201 ? PullBody : "{\"message\":\"Validation Failed\"}") : (200, "[]");
        return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
