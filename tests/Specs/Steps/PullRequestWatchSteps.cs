using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class PullRequestWatchSteps(ExceptionState exceptionState, ConsoleState consoleState) : IDisposable
{
    private readonly WatchHandler _handler = new();
    private string _owner = "";
    private string _repo = "";
    private int _number;
    private string _reviewer = "";
    private TimeSpan _wait = TimeSpan.Zero;
    private TimeSpan _timeout = TimeSpan.FromHours(1);
    private PullRequests? _pullRequests;
    private PullRequestWatch? _watch;
    private string? _sha;
    private PullRequestWatchResult? _result;

    public void Dispose() => _handler.Dispose();

    [Given("a PullRequests and a PullRequestWatch built over a GitHub client")]
    public void GivenBuilt()
    {
        var client = new GitHubClient(new HttpClient(_handler), "token", TimeSpan.Zero);
        _pullRequests = new PullRequests(client);
        _watch = new PullRequestWatch(_pullRequests, new ReviewThreads(client));
    }

    [Given("the watch is for the repo {string} of the owner {string}, the pull request {int}, the reviewer login {string}, a wait of zero between two polls and a timeout of 1 hour")]
    public void GivenWatchFor(string repo, string owner, int number, string reviewer)
    {
        _repo = repo;
        _owner = owner;
        _number = number;
        _reviewer = reviewer;
    }

    [Given("the watch is for the repo {string} of the owner {string}, the pull request {int}, the reviewer login {string}, a wait of zero between two polls and a timeout of zero")]
    public void GivenWatchForZero(string repo, string owner, int number, string reviewer)
    {
        GivenWatchFor(repo, owner, number, reviewer);
        _timeout = TimeSpan.Zero;
    }

    [Given("GitHub answers the read of the pull request with the head sha {string}")]
    public void GivenSha(string sha) => _handler.Sha = sha;

    [Given("GitHub answers the read of the pull request with the status code {int}")]
    public void GivenPullStatus(int status) => _handler.PullStatus = status;

    [Given("GitHub answers each read of the check runs with the check run {string} with the status {string} and the conclusion {string}")]
    public void GivenRun(string name, string status, string conclusion) =>
        _handler.CheckRunsBody = WatchHandler.RunsJson([(name, status, conclusion)]);

    [Given("GitHub answers each read of the check runs with the check run {string} with the status {string} and the conclusion {string}, and the check run {string} with the status {string} and the conclusion {string}")]
    public void GivenTwoRuns(string n1, string s1, string c1, string n2, string s2, string c2) =>
        _handler.CheckRunsBody = WatchHandler.RunsJson([(n1, s1, c1), (n2, s2, c2)]);

    [Given("GitHub answers each read of the check runs with the check run {string} with the status {string} and no conclusion")]
    public void GivenRunNoConclusion(string name, string status) =>
        _handler.CheckRunsBody = WatchHandler.RunsJson([(name, status, null)]);

    [Given("GitHub answers each read of the check runs with no check run")]
    public void GivenNoRun()
    {
        consoleState.NoCheckRuns = true;
        _handler.CheckRunsBody = WatchHandler.RunsJson([]);
    }

    [Given("GitHub answers the first read of the check runs with the check run {string} with the status {string} and no conclusion")]
    public void GivenFirstRun(string name, string status)
    {
        consoleState.FirstCheckRun = (name, status);
        _handler.CheckRunsAt(1, WatchHandler.RunsJson([(name, status, null)]));
    }

    [Given("GitHub answers the first read of the check runs with no check run")]
    public void GivenFirstNoRun() => _handler.CheckRunsAt(1, WatchHandler.RunsJson([]));

    [Given("GitHub answers the second read of the check runs with the check run {string} with the status {string} and the conclusion {string}")]
    public void GivenSecondRun(string name, string status, string conclusion) =>
        _handler.CheckRunsAt(2, WatchHandler.RunsJson([(name, status, conclusion)]));

    [Given("GitHub answers the second read of the check runs with the check run {string} with the status {string} and the conclusion {string}, and the check run {string} with the status {string} and the conclusion {string}")]
    public void GivenSecondTwoRuns(string n1, string s1, string c1, string n2, string s2, string c2) =>
        _handler.CheckRunsAt(2, WatchHandler.RunsJson([(n1, s1, c1), (n2, s2, c2)]));

    [Given("GitHub answers each read of the reviews with one review by {string} with the state {string} of the commit {string}")]
    public void GivenReview(string login, string state, string commit) => _handler.ReviewsBody = WatchHandler.ReviewsJson([(login, state, commit)]);

    [Given("GitHub answers each read of the reviews with no review")]
    public void GivenNoReview()
    {
        consoleState.NoReviews = true;
        _handler.ReviewsBody = WatchHandler.ReviewsJson([]);
    }

    [Given("GitHub answers the first read of the reviews with no review")]
    public void GivenFirstNoReview() => _handler.ReviewsAt(1, WatchHandler.ReviewsJson([]));

    [Given("GitHub answers the second read of the reviews with one review by {string} with the state {string} of the commit {string}")]
    public void GivenSecondReview(string login, string state, string commit) => _handler.ReviewsAt(2, WatchHandler.ReviewsJson([(login, state, commit)]));

    [Given("GitHub answers the read of the review threads with an empty list of threads")]
    public void GivenNoThreads() => _handler.ThreadsBody = WatchHandler.ThreadsJson([]);

    [Given("GitHub answers the read of the review threads with one unresolved thread with the id {string}, the path {string}, the line {int} and one comment by {string} with the body {string}")]
    public void GivenThread(string id, string path, int line, string login, string body) =>
        _handler.ThreadsBody = WatchHandler.ThreadsJson([(id, false, path, line, [(login, body)])]);

    [Given("GitHub answers the read of the review threads with the resolved thread {string} and the unresolved thread {string}")]
    public void GivenTwoThreads(string resolved, string unresolved) =>
        _handler.ThreadsBody = WatchHandler.ThreadsJson([(resolved, true, "a.txt", 1, [("u", "b")]), (unresolved, false, "a.txt", 2, [("u", "c")])]);

    [Given("GitHub answers the read of the review threads with the unresolved thread {string}")]
    public void GivenUnresolved(string id) =>
        _handler.ThreadsBody = WatchHandler.ThreadsJson([(id, false, "a.txt", 2, [("u", "c")])]);

    [Given("the timeout is zero")]
    public void GivenTimeoutZero() => _timeout = TimeSpan.Zero;

    [Given("the reviewer login is {string}")]
    public void GivenReviewer(string login) => _reviewer = login;

    [Given("the wait between two polls is 1 hour")]
    public void GivenWait() => _wait = TimeSpan.FromHours(1);

    [When("a caller reads the head commit of the pull request {int} of the repo {string} of the owner {string}")]
    public async Task WhenReadHead(int number, string repo, string owner)
    {
        try
        {
            _sha = await _pullRequests!.ReadHeadShaAsync(owner, repo, number);
        }
        catch (GitHubRequestException ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("a caller starts the watch")]
    public async Task WhenStart() => await Run(CancellationToken.None);

    [When("a caller starts the watch and cancels the cancellation token {int} second after the start")]
    public async Task WhenStartAndCancel(int seconds)
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        await Run(source.Token);
    }

    [Then("the recorded pull request read has the method {string} and the address {string}")]
    public void ThenPullRead(string method, string address)
    {
        RecordedRequest request = _handler.Requests.Single();
        Assert.Equal(method, request.Method);
        Assert.Equal(address, request.Uri);
    }

    [Then("the recorded check runs read has the method {string} and the address {string}")]
    public void ThenCheckRead(string method, string address)
    {
        RecordedRequest request = _handler.Requests.First(r => r.Uri.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.Equal(method, request.Method);
        Assert.Equal(address, request.Uri);
    }

    [Then("PullRequests returns the head sha {string}")]
    public void ThenSha(string sha) => Assert.Equal(sha, _sha);

    [Then("the watch gives back a green result")]
    public void ThenGreen() => Assert.True(_result!.IsGreen);

    [Then("the watch gives back a red result")]
    public void ThenRed() => Assert.False(_result!.IsGreen);

    [Then("the watch result holds an empty list of threads")]
    public void ThenEmpty() => Assert.Empty(_result!.Threads);

    [Then("the watch result holds one thread with the id {string}, the path {string}, the line {int} and one comment with the author {string} and the body {string}")]
    public void ThenThread(string id, string path, int line, string author, string body)
    {
        ReviewThread thread = Assert.Single(_result!.Threads);
        Assert.Equal(id, thread.Id);
        Assert.Equal(path, thread.Path);
        Assert.Equal(line, thread.Line);
        Assert.Equal(new ReviewThreadComment(author, body), Assert.Single(thread.Comments));
    }

    [Then("the watch result holds the one thread {string}")]
    public void ThenOneThread(string id) => Assert.Equal(id, Assert.Single(_result!.Threads).Id);

    [Then("the watch result holds the thread {string}")]
    public void ThenThreadId(string id) => Assert.Contains(_result!.Threads, t => t.Id == id);

    [Then("the watch throws a GitHubRequestException that gives the status code {int}")]
    public void ThenThrows(int status) =>
        Assert.Equal(status, Assert.IsType<GitHubRequestException>(exceptionState.Exception).StatusCode);

    [Then("the watch throws an OperationCanceledException")]
    public void ThenCanceled() => Assert.IsAssignableFrom<OperationCanceledException>(exceptionState.Exception);

    private async Task Run(CancellationToken token)
    {
        try
        {
            _result = await _watch!.WatchAsync(_owner, _repo, _number, _reviewer, _wait, _timeout, token);
        }
        catch (Exception ex) when (ex is GitHubRequestException or OperationCanceledException)
        {
            exceptionState.Exception = ex;
        }
    }
}
