using Xunit;

namespace Specs.Unit;

public sealed class ChecksTokenTests : IDisposable
{
    private const string Repo = "--repo sbiondolillo/factory-scratch";
    private readonly ConsoleHarness _h = new();

    public ChecksTokenTests() => _h.Environment["FACTORY_GITHUB_TOKEN"] = "github-token-1";

    public void Dispose() => _h.Dispose();

    private Task<int> StartAsync(string arguments) =>
        _h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);

    private IEnumerable<TimedRequest> Checks => _h.Requests.Where(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));

    [Theory]
    [InlineData("run", "--issue 12")]
    [InlineData("feedback", "--pr 10")]
    public async Task UnsetChecksTokenReadsWithTheGitHubToken(string verb, string number)
    {
        Assert.Equal(0, await StartAsync($"{verb} {Repo} {number}"));
        Assert.NotEmpty(Checks);
        Assert.All(_h.Requests, r => Assert.Equal("Bearer github-token-1", r.Authorization));
    }

    [Fact]
    public async Task EmptyChecksTokenIsTreatedAsUnset()
    {
        _h.Environment["FACTORY_CHECKS_TOKEN"] = "";
        Assert.Equal(0, await StartAsync($"feedback {Repo} --pr 10"));
        Assert.NotEmpty(Checks);
        Assert.All(_h.Requests, r => Assert.Equal("Bearer github-token-1", r.Authorization));
    }

    [Theory]
    [InlineData("run", "--issue 12")]
    [InlineData("feedback", "--pr 10")]
    public async Task ChecksTokenGoesOnlyToTheCheckRunsRead(string verb, string number)
    {
        _h.Environment["FACTORY_CHECKS_TOKEN"] = "checks-token-2";
        await StartAsync($"{verb} {Repo} {number}");
        Assert.NotEmpty(Checks);
        Assert.All(Checks, r => Assert.Equal("Bearer checks-token-2", r.Authorization));
        Assert.All(_h.Requests.Except(Checks), r => Assert.Equal("Bearer github-token-1", r.Authorization));
        Assert.DoesNotContain("checks-token-2", _h.Error.ToString(), StringComparison.Ordinal);
        Assert.All(_h.Commands, c => Assert.DoesNotContain("checks-token-2", c.Arguments));
    }

    [Fact]
    public async Task ARefusedCheckRunsReadEndsWithExitCode1()
    {
        _h.Environment["FACTORY_CHECKS_TOKEN"] = "checks-token-2";
        _h.Refusal = ("/check-runs", 403);
        Assert.Equal(1, await StartAsync($"feedback {Repo} --pr 10"));
        Assert.Equal(["GitHub refused a request with the status code 403."], ConsoleHarness.Lines(_h.Error));
    }

    [Fact]
    public void TheReadmeCheckFailsWhenTheLineIsAbsentOrBeforeTheStart()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsLineAfterStart("a\nb", "new", "a"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsLineAfterStart("new\nstart here", "new", "start"));
        WorkflowChecks.HoldsLineAfterStart("start here\nnew", "new", "start");
    }

    [Fact]
    public void TheHeaderCheckFailsOnNoRequestAndOnAnotherValue()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsHeader([], "Authorization", "Bearer a"));
        var ok = new TimedRequest("GET", "/x/check-runs", null, TimeSpan.Zero, "Bearer a");
        var other = new TimedRequest("GET", "/y/check-runs", null, TimeSpan.Zero, "Bearer b");
        WorkflowChecks.HoldsHeader([ok], "Authorization", "Bearer a");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsHeader([ok, other], "Authorization", "Bearer a"));
    }
}
