using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class GitHubClientTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task EveryAttemptTimingOutThrowsTimeoutExceptionAfterFourRequests()
    {
        using var handler = new RecordingHandler { Stalls = int.MaxValue };
        var client = new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero, Short);
        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(HttpMethod.Get, "/repos/o/r/issues/1", null, TestContext.Current.CancellationToken));
        Assert.Contains("GET", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/repos/o/r/issues/1", ex.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task CancelDuringAStalledAttemptIsNotRetried()
    {
        using var handler = new RecordingHandler { Stalls = int.MaxValue };
        using var source = new CancellationTokenSource();
        handler.OnRequest = _ => source.Cancel();
        var client = new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero, TimeSpan.FromSeconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(HttpMethod.Get, "/x", null, source.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TokenCancelledBeforeTheCallSendsNoRequest()
    {
        using var handler = new RecordingHandler();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var client = new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(HttpMethod.Get, "/x", null, source.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HttpClientTimeoutIsRetriedAndEndsInTimeoutException()
    {
        using var handler = new RecordingHandler { Throw = new TaskCanceledException("t", new TimeoutException()) };
        var client = new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero);
        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(HttpMethod.Get, "/x", null, TestContext.Current.CancellationToken));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public void TimeoutIsGivenOrThirtySeconds()
    {
        using var handler = new RecordingHandler();
        Assert.Equal(TimeSpan.FromSeconds(1), new GitHubClient(new HttpClient(handler), "t", null, TimeSpan.FromSeconds(1)).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new GitHubClient(new HttpClient(handler), "t").Timeout);
    }
}
