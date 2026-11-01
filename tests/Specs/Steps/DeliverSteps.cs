using System.Globalization;
using System.Text.Json;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class DeliverSteps(PullRequestAnswer answer, DeliverBodyState body) : IDisposable
{
    private readonly RoutingHandler _handler = new();
    private readonly List<CommandSpec> _commands = [];
    private string _workTree = "";
    private string _runId = "";
    private string _repo = "";
    private string _owner = "";
    private string _baseBranch = "";
    private int _issue;
    private string _title = "";
    private int _pushExit;
    private string _pushError = "fatal: push failed";
    private string _changedFile = "";
    private PullRequest? _result;
    private Exception? _exception;
    private DeliveryFacts? _facts;

    public void Dispose() => _handler.Dispose();

    [Given("the work tree {string} is the clone of the run {string} of the repo {string} of the owner {string}")]
    public void GivenWorkTree(string workTree, string runId, string repo, string owner)
    {
        _workTree = workTree;
        _runId = runId;
        _repo = repo;
        _owner = owner;
    }

    [Given("the base branch is {string} and the issue {int} has the title {string}")]
    public void GivenBase(string baseBranch, int issue, string title)
    {
        _baseBranch = baseBranch;
        _issue = issue;
        _title = title;
    }

    [Given("git answers each command with the exit code {int}")]
    public void GivenGitAnswers(int code) => _pushExit = code;

    [Given("git answers the push with the exit code {int}")]
    public void GivenPushAnswers(int code) => _pushExit = code;

    [Given("git answers the push with the exit code {int} and the standard error {string}")]
    public void GivenPushAnswersWithError(int code, string standardError)
    {
        _pushExit = code;
        _pushError = standardError;
    }

    [Given("the run changed the file {string}")]
    public void GivenChangedFile(string path) => _changedFile = path;

    [Given("GitHub answers the open of the pull request with the number {int} and the web address {string}")]
    public void GivenOpen(int number, string url)
    {
        answer.Number = number;
        answer.Url = url;
        _handler.PostBody = JsonSerializer.Serialize(new { number, html_url = url });
    }

    [Given("GitHub answers the open of the pull request with the status code {int}")]
    public void GivenOpenStatus(int status)
    {
        answer.Status = status;
        _handler.PostStatus = status;
        _handler.PostBody = "{\"message\":\"Validation Failed\"}";
    }

    [Given("the run spent {string} in {int} seconds over the session {string} of the role {string} at {int} cents and the session {string} of the role {string} at {int} cents")]
    public void GivenSessions(string spend, int seconds, string first, string firstRole, int firstCents, string second, string secondRole, int secondCents) =>
        _facts = new DeliveryFacts(
            Spend: decimal.Parse(spend, CultureInfo.InvariantCulture),
            Seconds: seconds,
            Sessions: [new SessionCost(first, firstRole, firstCents), new SessionCost(second, secondRole, secondCents)]);

    [Given("the run spent {string} in {int} seconds over no session")]
    public void GivenNoSession(string spend, int seconds) =>
        _facts = new DeliveryFacts(Spend: decimal.Parse(spend, CultureInfo.InvariantCulture), Seconds: seconds, Sessions: []);

    [When("a caller delivers the run with the token {string}")]
    public async Task WhenDelivers(string token)
    {
        if (body.InUse)
        {
            body.Result = await body.Harness.RunAsync();
            Assert.Equal(body.ExpectedRedos, body.Result.Redos);
            body.Body = body.Harness.PullBody ?? "";
            return;
        }

        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _commands.Add(spec);
                int exit = spec.Arguments[0] is "push" ? _pushExit : 0;
                return Task.FromResult(new CommandResult { ExitCode = exit, StandardError = exit is 0 ? "" : _pushError });
            });
        var client = new GitHubClient(new HttpClient(_handler), token, TimeSpan.Zero);
        var deliver = new Deliver(new Git(mock.Object), new PullRequests(client), client, _facts);
        try
        {
            _result = await deliver.RunAsync(_workTree, _runId, _owner, _repo, _baseBranch, _issue, _title, token);
        }
        catch (Exception ex) when (ex is GitCommandException or GitHubRequestException)
        {
            _exception = ex;
        }

        Assert.DoesNotContain(_commands, c => _changedFile.Length > 0 && c.Arguments.Contains(_changedFile));
    }

    [Then("git pushed the branch {string} from {string} to origin")]
    public void ThenPushed(string branch, string workTree)
    {
        CommandSpec push = Assert.Single(_commands, c => c.Arguments[0] is "push");
        Assert.Equal(["push", "origin", branch], push.Arguments);
        Assert.Equal(workTree, push.WorkingDirectory);
    }

    [Then("Deliver opened a pull request on {string} from the branch {string} into {string} with the title {string}")]
    public void ThenOpened(string slug, string head, string baseBranch, string title)
    {
        RecordedRequest post = OpenRequest();
        Assert.EndsWith($"/repos/{slug}/pulls", post.Uri, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(post.Content!);
        Assert.Equal(head, json.RootElement.GetProperty("head").GetString());
        Assert.Equal(baseBranch, json.RootElement.GetProperty("base").GetString());
        Assert.Equal(title, json.RootElement.GetProperty("title").GetString());
    }

    [Then("the first line of the body of the pull request is {string}")]
    public void ThenBody(string line)
    {
        using var json = JsonDocument.Parse(OpenRequest().Content!);
        string body = json.RootElement.GetProperty("body").GetString() ?? "";
        Assert.Equal(line, body.Split('\n')[0]);
    }

    [Then("the last two lines of the body of the pull request are {string} and {string}")]
    public void ThenLastTwo(string first, string second)
    {
        using var json = JsonDocument.Parse(OpenRequest().Content!);
        string[] lines = (json.RootElement.GetProperty("body").GetString() ?? "").Split('\n');
        Assert.Equal([first, second], lines[^2..]);
    }

    [Then("the last line of the body of the pull request that Deliver opened is {string}")]
    public void ThenLastOpened(string line)
    {
        using var json = JsonDocument.Parse(OpenRequest().Content!);
        Assert.Equal(line, (json.RootElement.GetProperty("body").GetString() ?? "").Split('\n')[^1]);
    }

    [Then("Deliver posted one comment on the issue {int} of {string} that holds {string}")]
    public void ThenComment(int issue, string slug, string text)
    {
        RecordedRequest comment = Assert.Single(Comments());
        Assert.EndsWith($"/repos/{slug}/issues/{issue}/comments", comment.Uri, StringComparison.Ordinal);
        Assert.Contains(text, comment.Content, StringComparison.Ordinal);
    }

    [Then("Deliver gives back the pull request with the number {int} and the web address {string}")]
    public void ThenResult(int number, string url) => Assert.Equal(new PullRequest(number, url), _result);

    [Then("Deliver throws GitCommandException")]
    public void ThenGitException() => Assert.IsType<GitCommandException>(_exception);

    [Then("Deliver throws GitCommandException whose message holds {string}")]
    public void ThenGitExceptionHolds(string text) =>
        Assert.Contains(text, Assert.IsType<GitCommandException>(_exception).Message, StringComparison.Ordinal);

    [Then("Deliver sent no request to GitHub")]
    public void ThenNoRequest() => Assert.Empty(_handler.Requests);

    [Then("Deliver throws GitHubRequestException with the status code {int}")]
    public void ThenGitHubException(int status) => Assert.Equal(status, Assert.IsType<GitHubRequestException>(_exception).StatusCode);

    [Then("Deliver posted no comment")]
    public void ThenNoComment() => Assert.Empty(Comments());

    private RecordedRequest OpenRequest() =>
        Assert.Single(_handler.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));

    private List<RecordedRequest> Comments() =>
        [.. _handler.Requests.Where(r => r.Uri.EndsWith("/comments", StringComparison.Ordinal))];
}
