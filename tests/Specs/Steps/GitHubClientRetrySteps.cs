using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitHubClientRetrySteps(ExceptionState exceptionState, RecordingHandler handler, ClientState state)
{
    private const string Json = "{\"ok\":\"yes\"}";
    private readonly RecordingHandler _handler = handler;

    [Given("a GitHub client with a base wait of zero")]
    public void GivenClient() => state.Client = new GitHubClient(new HttpClient(_handler), "token", TimeSpan.Zero);

    [Given("a GitHub client with a base wait of zero and the token {string}")]
    public void GivenClientWithToken(string token) => state.Client = new GitHubClient(new HttpClient(_handler), token, TimeSpan.Zero);

    [Given("GitHub answers with the statuses {int}, {int} and {int} with a JSON body")]
    public void GivenThree(int a, int b, int c)
    {
        _handler.Enqueue(a, "x");
        _handler.Enqueue(b, "x");
        _handler.Answer(c, Json);
    }

    [Given("GitHub answers with the statuses {int} and {int} with a JSON body")]
    public void GivenTwo(int a, int b)
    {
        _handler.Enqueue(a, "x");
        _handler.Answer(b, Json);
    }

    [Given("GitHub answers every request with the status {int}")]
    public void GivenEvery(int status) => _handler.Answer(status, "body");

    [Given("GitHub answers with a 403 that holds a Retry-After header of 0 seconds and then a 200 with a JSON body")]
    public void GivenRetryAfter()
    {
        _handler.Enqueue(403, "x", 0);
        _handler.Answer(200, Json);
    }

    [Given("GitHub answers every request with a 403 that holds no Retry-After header")]
    public void GivenNoRetryAfter() => _handler.Answer(403, "forbidden");

    [Then("SendAsync gives back the JSON body of the successful answer")]
    public void ThenJson()
    {
        Assert.Null(exceptionState.Exception);
        Assert.Equal("yes", state.Returned.GetProperty("ok").GetString());
    }

    [Then("SendAsync throws GitHubRequestException whose status code is {int}")]
    public void ThenThrows(int status) => Assert.Equal(status, Assert.IsType<GitHubRequestException>(exceptionState.Exception).StatusCode);

    [Then("GitHub received {int} request(s)")]
    public void ThenReceived(int count) => Assert.Equal(count, _handler.Requests.Count);

    [Then("the second recorded request has the same method, path, bearer token, user agent and JSON body as the first")]
    public void ThenSame()
    {
        Assert.Equal(2, _handler.Requests.Count);
        RecordedRequest first = _handler.Requests[0];
        RecordedRequest second = _handler.Requests[1];
        Assert.Equal(first.Method, second.Method);
        Assert.Equal(first.Uri, second.Uri);
        Assert.Equal("Bearer token-9", second.Headers["Authorization"]);
        Assert.Equal(first.Headers["Authorization"], second.Headers["Authorization"]);
        Assert.Equal(first.Headers["User-Agent"], second.Headers["User-Agent"]);
        Assert.False(string.IsNullOrEmpty(second.Content));
        Assert.Equal(first.Content, second.Content);
    }
}
