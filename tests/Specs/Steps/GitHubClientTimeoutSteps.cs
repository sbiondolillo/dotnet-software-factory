using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitHubClientTimeoutSteps(ExceptionState exceptionState, RecordingHandler handler, ClientState state)
{
    [Given("a GitHub client with a base wait of zero and a timeout of {int} second(s) for one request")]
    public void GivenClient(int seconds) =>
        state.Client = new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero, TimeSpan.FromSeconds(seconds));

    [Given("GitHub gives no answer to the first request and answers the second with 200 and a JSON body")]
    public void GivenFirstStalls()
    {
        handler.Stalls = 1;
        handler.Answer(200, "{\"ok\":\"yes\"}");
    }

    [Given("GitHub gives no answer to any request")]
    public void GivenNoAnswer() => handler.Stalls = int.MaxValue;

    [When("I send a {string} request for the path {string} and the caller cancels its token during the second request")]
    public async Task WhenCancelDuringSecond(string method, string path)
    {
        using var source = new CancellationTokenSource();
        handler.OnRequest = n =>
        {
            if (n is 2)
            {
                source.Cancel();
            }
        };
        try
        {
            await state.Client!.SendAsync(new HttpMethod(method), path, null, source.Token);
        }
        catch (OperationCanceledException ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("SendAsync throws a TimeoutException whose message holds {string} and {string}")]
    public void ThenTimeout(string method, string path)
    {
        TimeoutException ex = Assert.IsType<TimeoutException>(exceptionState.Exception);
        Assert.Contains(method, ex.Message, StringComparison.Ordinal);
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Then("SendAsync throws an OperationCanceledException")]
    public void ThenCanceled() => Assert.IsAssignableFrom<OperationCanceledException>(exceptionState.Exception);

    [Given("a GitHub client built with no timeout")]
    public void GivenNoTimeout() => state.Client = new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero);

    [Then("the client reports a timeout of {int} seconds for one request")]
    public void ThenReports(int seconds) => Assert.Equal(TimeSpan.FromSeconds(seconds), state.Client!.Timeout);
}
