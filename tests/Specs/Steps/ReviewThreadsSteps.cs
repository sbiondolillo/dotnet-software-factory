using System.Text.Json;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ReviewThreadsSteps(ExceptionState exceptionState, RecordingHandler handler)
{
    private ReviewThreads? _sut;
    private IReadOnlyList<Review>? _reviews;
    private IReadOnlyList<ReviewThread>? _threads;

    [Given("a ReviewThreads built over a GitHub client")]
    public void GivenBuilt() => _sut = new ReviewThreads(new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero));

    [Given("GitHub answers the read of reviews with an empty list of reviews")]
    public void GivenNoReviews() => handler.Answer(200, ReviewsJson([]));

    [Given("GitHub answers the read of reviews with one review with the author login {string} and the state {string}")]
    public void GivenOneReview(string login, string state) =>
        handler.Answer(200, ReviewsJson([$"{{\"state\":{Q(state)},\"author\":{{\"login\":{Q(login)}}}}}"]));

    [Given("GitHub answers the read of review threads with an empty list of threads")]
    public void GivenNoThreads() => handler.Answer(200, ThreadsJson([]));

    [Given("GitHub answers the read of review threads with one unresolved thread with the id {string}, the path {string}, the line {int} and one comment with the author login {string} and the body {string}")]
    public void GivenOneThread(string id, string path, int line, string login, string body) =>
        handler.Answer(200, ThreadsJson([ThreadJson(id, false, path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), [(login, body)])]));

    [Given("GitHub answers the read of review threads with a thread whose isResolved is true")]
    public void GivenResolved() =>
        handler.Answer(200, ThreadsJson([ThreadJson("PRRT_1", true, "a.txt", "1", [("u", "b")])]));

    [Given("GitHub answers the read of review threads with a thread whose comments are first {string} and then {string}")]
    public void GivenTwoComments(string first, string second) =>
        handler.Answer(200, ThreadsJson([ThreadJson("PRRT_1", false, "a.txt", "1", [("u", first), ("u", second)])]));

    [Given("GitHub answers the read of review threads with a thread whose line is null")]
    public void GivenNullLine() =>
        handler.Answer(200, ThreadsJson([ThreadJson("PRRT_1", false, "a.txt", "null", [("u", "b")])]));

    [Given("GitHub answers the reply to a thread with success")]
    public void GivenReplyOk() =>
        handler.Answer(200, "{\"data\":{\"addPullRequestReviewThreadReply\":{\"comment\":{\"id\":\"PRRC_1\"}}}}");

    [Given("GitHub answers the resolve of a thread with success")]
    public void GivenResolveOk() =>
        handler.Answer(200, "{\"data\":{\"resolveReviewThread\":{\"thread\":{\"isResolved\":true}}}}");

    [Given("GitHub answers the request with the status code {int} and a list errors holding the message {string}")]
    public void GivenErrors(int status, string message) =>
        handler.Answer(status, $"{{\"data\":null,\"errors\":[{{\"message\":{Q(message)}}}]}}");

    [Given("GitHub answers the request with the status code {int}")]
    public void GivenStatus(int status) => handler.Answer(status, "{\"message\":\"Bad credentials\"}");

    [When("a caller reads the reviews of pull request {int} of the repo {string} of the owner {string}")]
    public async Task WhenReadReviews(int number, string repo, string owner) =>
        await Try(async () => _reviews = await _sut!.ReadReviewsAsync(owner, repo, number));

    [When("a caller reads the review threads of pull request {int} of the repo {string} of the owner {string}")]
    public async Task WhenReadThreads(int number, string repo, string owner) =>
        await Try(async () => _threads = await _sut!.ReadThreadsAsync(owner, repo, number));

    [When("a caller replies to the thread {string} with the text {string}")]
    public async Task WhenReply(string threadId, string text) =>
        await Try(() => _sut!.ReplyAsync(threadId, text));

    [When("a caller resolves the thread {string}")]
    public async Task WhenResolve(string threadId) =>
        await Try(() => _sut!.ResolveAsync(threadId));

    [Then("the content of the recorded request is JSON whose query names {string}")]
    public void ThenQueryNames(string name)
    {
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        Assert.Contains(name, document.RootElement.GetProperty("query").GetString());
    }

    [Then("the variables of the recorded request hold the owner {string}, the name {string} and the number {int}")]
    public void ThenOwnerNameNumber(string owner, string name, int number)
    {
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        JsonElement variables = document.RootElement.GetProperty("variables");
        Assert.Equal(owner, variables.GetProperty("owner").GetString());
        Assert.Equal(name, variables.GetProperty("name").GetString());
        Assert.Equal(number, variables.GetProperty("number").GetInt32());
    }

    [Then("the variables of the recorded request hold the threadId {string} and the body {string}")]
    public void ThenThreadIdBody(string threadId, string body)
    {
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        JsonElement variables = document.RootElement.GetProperty("variables");
        Assert.Equal(threadId, variables.GetProperty("threadId").GetString());
        Assert.Equal(body, variables.GetProperty("body").GetString());
    }

    [Then("the variables of the recorded request hold the threadId {string}")]
    public void ThenThreadId(string threadId)
    {
        using JsonDocument document = JsonDocument.Parse(handler.Requests[^1].Content!);
        Assert.Equal(threadId, document.RootElement.GetProperty("variables").GetProperty("threadId").GetString());
    }

    [Then("ReviewThreads returns one review with the author {string} and the state {string}")]
    public void ThenOneReview(string author, string state) =>
        Assert.Equal(new Review(author, state), Assert.Single(_reviews!));

    [Then("ReviewThreads returns an empty list of reviews")]
    public void ThenNoReviews() => Assert.Empty(_reviews!);

    [Then("ReviewThreads returns one unresolved thread with the id {string}, the path {string}, the line {int} and one comment with the author {string} and the body {string}")]
    public void ThenOneThread(string id, string path, int line, string author, string body)
    {
        ReviewThread thread = Assert.Single(_threads!);
        Assert.Equal(id, thread.Id);
        Assert.False(thread.IsResolved);
        Assert.Equal(path, thread.Path);
        Assert.Equal(line, thread.Line);
        Assert.Equal(new ReviewThreadComment(author, body), Assert.Single(thread.Comments));
    }

    [Then("ReviewThreads returns a resolved thread")]
    public void ThenResolved() => Assert.True(Assert.Single(_threads!).IsResolved);

    [Then("ReviewThreads returns a thread whose first comment has the body {string} and whose second comment has the body {string}")]
    public void ThenOrder(string first, string second)
    {
        IReadOnlyList<ReviewThreadComment> comments = Assert.Single(_threads!).Comments;
        Assert.Equal(2, comments.Count);
        Assert.Equal(first, comments[0].Body);
        Assert.Equal(second, comments[1].Body);
    }

    [Then("ReviewThreads returns a thread whose line is null")]
    public void ThenNullLine() => Assert.Null(Assert.Single(_threads!).Line);

    [Then("ReviewThreads returns an empty list of threads")]
    public void ThenNoThreads() => Assert.Empty(_threads!);

    [Then("ReviewThreads throws a GitHubGraphQLException whose message holds {string}")]
    public void ThenGraphQl(string text) =>
        Assert.Contains(text, Assert.IsType<GitHubGraphQLException>(exceptionState.Exception).Message);

    [Then("ReviewThreads throws a GitHubRequestException that gives the status code {int}")]
    public void ThenRequestException(int status) =>
        Assert.Equal(status, Assert.IsType<GitHubRequestException>(exceptionState.Exception).StatusCode);

    private static string Q(string value) => JsonSerializer.Serialize(value);

    private static string ReviewsJson(string[] nodes) =>
        $"{{\"data\":{{\"repository\":{{\"pullRequest\":{{\"reviews\":{{\"nodes\":[{string.Join(",", nodes)}]}}}}}}}}}}";

    private static string ThreadsJson(string[] nodes) =>
        $"{{\"data\":{{\"repository\":{{\"pullRequest\":{{\"reviewThreads\":{{\"nodes\":[{string.Join(",", nodes)}]}}}}}}}}}}";

    private static string ThreadJson(string id, bool resolved, string path, string line, (string Login, string Body)[] comments)
    {
        string nodes = string.Join(",", comments.Select(c => $"{{\"author\":{{\"login\":{Q(c.Login)}}},\"body\":{Q(c.Body)}}}"));
        return $"{{\"id\":{Q(id)},\"isResolved\":{(resolved ? "true" : "false")},\"path\":{Q(path)},\"line\":{line},\"comments\":{{\"nodes\":[{nodes}]}}}}";
    }

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is GitHubGraphQLException or GitHubRequestException)
        {
            exceptionState.Exception = ex;
        }
    }
}
