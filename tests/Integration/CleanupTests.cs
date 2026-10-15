using Xunit;

namespace Integration;

/// <summary>Proves the order and the errors of <see cref="Cleanup"/>, which every test of the suite that creates a thing runs.</summary>
public sealed class CleanupTests
{
    [Fact]
    public async Task EveryStepRunsLastRecordedFirstAndAFailedBodyKeepsItsException()
    {
        var ran = new List<string>();
        var body = new InvalidOperationException("the body");
        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Cleanup.RunAsync(cleanup =>
        {
            cleanup.Add("first", () => ran.Add("first"));
            cleanup.Add("second", () => throw new InvalidOperationException("the step"));
            cleanup.Add("third", () =>
            {
                ran.Add("third");
                return Task.CompletedTask;
            });
            throw body;
        }));

        Assert.Same(body, thrown);
        Assert.Equal(["third", "first"], ran);
    }

    [Fact]
    public async Task AFailedStepAfterAPassedBodyFailsWithEveryStepError()
    {
        var ran = new List<string>();
        AggregateException thrown = await Assert.ThrowsAsync<AggregateException>(() => Cleanup.RunAsync(cleanup =>
        {
            cleanup.Add("delete the directory", () => throw new IOException("busy"));
            cleanup.Add("close the issue", () => ran.Add("close"));
            cleanup.Add("delete the branch", () => throw new HttpRequestException("422"));
            return Task.CompletedTask;
        }));

        Assert.Equal(["close"], ran);
        Assert.Equal(
            ["The cleanup step 'delete the branch' failed: 422", "The cleanup step 'delete the directory' failed: busy"],
            thrown.InnerExceptions.Select(e => e.Message));
    }

    [Fact]
    public async Task APassedBodyWithPassedStepsPasses()
    {
        var ran = new List<string>();
        await Cleanup.RunAsync(cleanup =>
        {
            cleanup.Add("one", () => ran.Add("one"));
            return Task.CompletedTask;
        });

        Assert.Equal(["one"], ran);
    }
}
