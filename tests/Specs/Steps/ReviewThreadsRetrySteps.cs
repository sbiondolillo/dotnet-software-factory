using System.Text.Json;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ReviewThreadsRetrySteps(ExceptionState exceptionState, RecordingHandler handler, ReviewThreadsSteps shared)
{
    private bool _returned;

    [Given("GitHub answers first with the status {int} and a list errors holding the message {string}")]
    public void GivenFirst(int status, string message) => handler.Enqueue(status, ErrorsJson(message));

    [Given("GitHub answers every request with the status {int} and a list errors holding the message {string}")]
    public void GivenEvery(int status, string message) => handler.Answer(status, ErrorsJson(message));

    [Given("GitHub then answers with the status {int} and a list errors holding the message {string}")]
    public void GivenThenErrors(int status, string message) => handler.Answer(status, ErrorsJson(message));

    [Given("GitHub then answers with a good answer")]
    public void GivenThenGood() => handler.Answer(200, "{\"data\":{}}");

    [Given("GitHub then answers the read of threads with one unresolved thread with the id {string}")]
    public void GivenThenThread(string id) =>
        handler.Answer(200, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[{\"id\":" + Q(id) + ",\"isResolved\":false,\"path\":\"a.txt\",\"line\":1,\"comments\":{\"nodes\":[]}}]}}}}}");

    [Given("GitHub then answers the read of reviews with one review with the author login {string}")]
    public void GivenThenReview(string login) =>
        handler.Answer(200, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviews\":{\"nodes\":[{\"state\":\"APPROVED\",\"author\":{\"login\":" + Q(login) + "},\"commit\":{\"oid\":\"abc\"}}]}}}}}");

    [When("a caller requests a review on pull request {int} of the repo {string} of the owner {string}")]
    public async Task WhenRequest(int number, string repo, string owner)
    {
        await Try(() => Sut().RequestReviewAsync($"{owner}/{repo}#{number}", "bot"));
    }

    [Then("ReviewThreads returns one thread with the id {string}")]
    public void ThenThread(string id) => Assert.Equal(id, Assert.Single(shared.Threads!).Id);

    [Then("ReviewThreads returns one review with the author {string}")]
    public void ThenReview(string author) => Assert.Equal(author, Assert.Single(shared.Reviews!).Author);

    [Then("RequestReviewAsync returns with no exception")]
    public void ThenRequestOk() => Assert.Null(exceptionState.Exception);

    [Then("ReplyAsync returns with no exception")]
    public void ThenReplyOk() => Assert.Null(exceptionState.Exception);

    [Then("ResolveAsync returns with no exception")]
    public void ThenResolveOk() => Assert.Null(exceptionState.Exception);

    [Then("ReviewThreads throws a GitHubGraphQLException")]
    public void ThenThrows() => Assert.IsType<GitHubGraphQLException>(exceptionState.Exception);

    [Then("the message of the exception holds {string}")]
    public void ThenMessage(string text) =>
        Assert.Contains(text, Assert.IsType<GitHubGraphQLException>(exceptionState.Exception).Message);

    private static string Q(string value) => JsonSerializer.Serialize(value);

    private static string ErrorsJson(string message) => $"{{\"data\":null,\"errors\":[{{\"message\":{Q(message)}}}]}}";

    private ReviewThreads Sut() => new(new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero));

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
            _returned = true;
        }
        catch (GitHubGraphQLException ex)
        {
            exceptionState.Exception = ex;
        }

        Assert.True(_returned || exceptionState.Exception is not null);
    }
}
