using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class DeliverSteps : IDisposable
{
    private readonly List<CommandSpec> _commands = [];
    private readonly List<(string Method, string Path, string? Content)> _requests = [];
    private readonly HttpClient _http;
    private string _workTree = "";
    private string _runId = "";
    private string _repo = "";
    private string _owner = "";
    private string _base = "";
    private string _token = "";
    private int _issue;
    private string _title = "";
    private string? _failingSubcommand;
    private int _openStatus = 201;
    private string _openBody = "{}";
    private PullRequest? _result;
    private Exception? _exception;

    public DeliverSteps() => _http = new HttpClient(new Handler(this));

    public void Dispose() => _http.Dispose();

    [Given("the work tree {string}, the run id {string}, the repo {string} of the owner {string}, the base branch {string} and the token {string}")]
    public void GivenTheRun(string workTree, string runId, string repo, string owner, string baseBranch, string token)
    {
        _workTree = workTree;
        _runId = runId;
        _repo = repo;
        _owner = owner;
        _base = baseBranch;
        _token = token;
    }

    [Given("the issue is {int} with the title {string}")]
    public void GivenTheIssue(int number, string title)
    {
        _issue = number;
        _title = title;
    }

    [Given("git answers each command with the exit code 0")]
    public void GivenGitAnswersZero() => _failingSubcommand = null;

    [Given("git answers the push with the exit code 1")]
    public void GivenGitPushFails() => _failingSubcommand = "push";

    [Given("GitHub answers the open of the pull request with the number {int} and the web address {string}")]
    public void GivenOpenAnswer(int number, string url)
    {
        _openStatus = 201;
        _openBody = JsonSerializer.Serialize(new { number, html_url = url });
    }

    [Given("GitHub answers the open of the pull request with the status code {int}")]
    public void GivenOpenStatus(int status)
    {
        _openStatus = status;
        _openBody = "{\"message\":\"Validation Failed\"}";
    }

    [When("a caller delivers the run")]
    public async Task WhenDelivers()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(m => m.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _commands.Add(spec);
                return Task.FromResult(new CommandResult { ExitCode = spec.Arguments[0] == _failingSubcommand ? 1 : 0, StandardError = "failed" });
            });
        var client = new GitHubClient(_http, _token, TimeSpan.Zero);
        var deliver = new Deliver(new Git(mock.Object), new PullRequests(client), client);
        try
        {
            _result = await deliver.DeliverAsync(_workTree, _runId, _owner, _repo, _base, _issue, _title, _token);
        }
        catch (Exception ex) when (ex is GitCommandException or GitHubRequestException)
        {
            _exception = ex;
        }
    }

    [Then("git pushes the branch {string} from {string} to origin")]
    public void ThenPushes(string branch, string directory)
    {
        CommandSpec push = _commands.Single(c => c.Arguments[0] is "push");
        Assert.Equal(["push", "origin", branch], push.Arguments);
        Assert.Equal(directory, push.WorkingDirectory);
    }

    [Then("Deliver opens a pull request on {string} from the branch {string} into {string} with the title {string}")]
    public void ThenOpens(string repoPath, string head, string baseBranch, string title)
    {
        var post = _requests.Single(r => r.Method is "POST" && r.Path.EndsWith("/pulls", StringComparison.Ordinal));
        Assert.Equal($"/repos/{repoPath}/pulls", post.Path);
        using JsonDocument doc = JsonDocument.Parse(post.Content!);
        Assert.Equal(head, doc.RootElement.GetProperty("head").GetString());
        Assert.Equal(baseBranch, doc.RootElement.GetProperty("base").GetString());
        Assert.Equal(title, doc.RootElement.GetProperty("title").GetString());
    }

    [Then("the body of the pull request starts with the line {string}")]
    public void ThenBody(string line)
    {
        var post = _requests.Single(r => r.Method is "POST" && r.Path.EndsWith("/pulls", StringComparison.Ordinal));
        using JsonDocument doc = JsonDocument.Parse(post.Content!);
        string first = doc.RootElement.GetProperty("body").GetString()!.Split('\n')[0];
        Assert.Equal(line, first);
    }

    [Then("Deliver posts one comment on the issue {int} of {string}")]
    public void ThenComment(int number, string repoPath)
    {
        var comment = Assert.Single(_requests, r => r.Path.EndsWith("/comments", StringComparison.Ordinal));
        Assert.Equal($"/repos/{repoPath}/issues/{number}/comments", comment.Path);
    }

    [Then("the comment holds {string}")]
    public void ThenCommentHolds(string text)
    {
        var comment = Assert.Single(_requests, r => r.Path.EndsWith("/comments", StringComparison.Ordinal));
        using JsonDocument doc = JsonDocument.Parse(comment.Content!);
        Assert.Contains(text, doc.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    [Then("Deliver gives back the pull request with the number {int} and the web address {string}")]
    public void ThenGivesBack(int number, string url)
    {
        Assert.Equal(new PullRequest(number, url), _result);
    }

    [Then("Deliver throws GitCommandException")]
    public void ThenThrowsGit() => Assert.IsType<GitCommandException>(_exception);

    [Then("no request was sent to GitHub")]
    public void ThenNoRequest() => Assert.Empty(_requests);

    [Then("Deliver throws GitHubRequestException with the status code {int}")]
    public void ThenThrowsGitHub(int status) =>
        Assert.Equal(status, Assert.IsType<GitHubRequestException>(_exception).StatusCode);

    [Then("no comment was posted")]
    public void ThenNoComment() =>
        Assert.DoesNotContain(_requests, r => r.Path.EndsWith("/comments", StringComparison.Ordinal));

    private sealed class Handler(DeliverSteps steps) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri!.AbsolutePath;
            steps._requests.Add((request.Method.Method, path, content));
            (int status, string body) = request.Method == HttpMethod.Get
                ? (200, "[]")
                : path.EndsWith("/comments", StringComparison.Ordinal) ? (201, "{}") : (steps._openStatus, steps._openBody);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
