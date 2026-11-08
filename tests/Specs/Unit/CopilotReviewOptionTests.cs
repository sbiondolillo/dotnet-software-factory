using Xunit;

namespace Specs.Unit;

public sealed class CopilotReviewOptionTests : IDisposable
{
    private readonly ConsoleHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData("run --repo a/b --issue 1 --copilot-review maybe")]
    [InlineData("feedback --repo a/b --pr 1 --copilot-review maybe")]
    [InlineData("run --repo a/b --issue 1 --copilot-review ON")]
    [InlineData("feedback --repo a/b --pr 1 --copilot-review ON")]
    public async Task AValueThatIsNeitherOnNorOffIsRefusedWithOneLine(string arguments)
    {
        int exit = await _h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);
        Assert.Equal(1, exit);
        Assert.Contains("--copilot-review", Assert.Single(ConsoleHarness.Lines(_h.Error)), StringComparison.Ordinal);
        Assert.Empty(_h.Requests);
    }

    [Theory]
    [InlineData("run --repo a/b --issue 1 --copilot-review off")]
    [InlineData("feedback --repo a/b --pr 10 --copilot-review off")]
    public async Task WithTheReviewOffNoReviewRequestGoesToGitHub(string arguments)
    {
        _h.Reviews = ConsoleHarness.NoReviewsJson();
        await _h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));
    }
}
