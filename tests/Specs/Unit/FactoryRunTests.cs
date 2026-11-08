using System.Text;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FactoryRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Solution.slnx")]
    [InlineData("specs/features/")]
    [InlineData("tests/Specs/")]
    [InlineData("tests/Integration/")]
    public async Task AnAbsentPathIsNamedInOneCommentAndNoContainerStarts(string path)
    {
        using var h = new RunHarness();
        h.Lacks.Add(path);
        RunResult<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Contains(path, Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Null(result.Passed);
        Assert.Equal(0, result.Redos);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
        Assert.Empty(h.AgentRequests);
    }

    [Fact]
    public async Task TwoAbsentPathsGetOneCommentNamingBoth()
    {
        using var h = new RunHarness();
        h.Lacks.Add("Solution.slnx");
        h.Lacks.Add("tests/Specs/");
        await h.RunFactoryAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains("Solution.slnx", body, StringComparison.Ordinal);
        Assert.Contains("tests/Specs/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathOfTheWrongKindCountsAsAbsent()
    {
        using var h = new RunHarness();
        h.Lacks.Add("Solution.slnx");
        h.Lacks.Add("specs/features/");
        h.AfterClone = directory =>
        {
            Directory.CreateDirectory(Path.Combine(directory, "Solution.slnx"));
            Directory.CreateDirectory(Path.Combine(directory, "specs"));
            File.WriteAllText(Path.Combine(directory, "specs", "features"), "");
        };
        await h.RunFactoryAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains("Solution.slnx", body, StringComparison.Ordinal);
        Assert.Contains("specs/features/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLayoutCheckRunsNoCommand()
    {
        using var h = new RunHarness();
        h.Lacks.Add("Solution.slnx");
        await h.RunFactoryAsync(Ct);
        Assert.Equal(["clone", "rev-parse"], h.Commands.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task AFailedPlanPhaseCommentsRunsNoBuildAndRemovesTheContainer()
    {
        using var h = new RunHarness();
        h.Builder.Clear(Phase.Plan);
        RunResult<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Contains(".factory/plan.md", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Build);
        Assert.Null(result.Passed);
        Assert.Equal(0, result.Redos);
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task FourRedGatesEndTheRunWithOneCommentAndNoPush()
    {
        using var h = new RunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        RunResult<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Equal(4, h.Builder.Of(Phase.Build).Count());
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Review);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains(RunHarness.RedText, body, StringComparison.Ordinal);
        Assert.Contains("spent", body, StringComparison.Ordinal);
        Assert.DoesNotContain("push", h.Events);
        Assert.Null(result.Passed);
        Assert.Equal(3, result.Redos);
    }

    [Fact]
    public async Task AnErrorOnThePlainPlanPromptPostsItsTextAndRunsNoBuild()
    {
        using var h = new RunHarness();
        h.Builder.Answer(Phase.Plan, FakeAgent.Error("plan went wrong"));
        RunResult<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Null(result.Passed);
        Assert.Equal(0, result.Redos);
        Assert.Equal(["plan"], h.AgentRequests.Select(r => r.Phase.Text()));
        Assert.Contains("plan went wrong", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErrorOnTheRefactorPlanPromptSendsOnePrompt()
    {
        using var h = new RunHarness();
        h.Labels.Add("refactor");
        h.Builder.Answer(Phase.Plan, FakeAgent.Error("plan went wrong"));
        RunResult<Delivered> result = await h.RunFactoryAsync(Ct);
        Assert.Null(result.Passed);
        Assert.Equal(["plan"], h.AgentRequests.Select(r => r.Phase.Text()));
    }

    [Fact]
    public async Task AnOverLimitCommentIsCutByBytesAndKeepsTwoHundredThousandLetters()
    {
        using var h = new RunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        h.TestStdout = new string('a', 300000);
        await h.RunFactoryAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 262144);
        Assert.Contains(new string('a', 200000), body, StringComparison.Ordinal);
        Assert.Contains("[cut]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMultiByteCommentIsCutOnWholeCharacters()
    {
        using var h = new RunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        h.TestStdout = new string('é', 200000);
        await h.RunFactoryAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 262144);
        Assert.DoesNotContain('\uFFFD', body);
        Assert.Contains("[cut]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommentUnderTheLimitIsPostedWhole()
    {
        using var h = new RunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        h.TestStdout = new string('a', 1000);
        await h.RunFactoryAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains(new string('a', 1000), body, StringComparison.Ordinal);
        Assert.DoesNotContain("[cut]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedPullRequestThrowsAndRemovesTheContainer()
    {
        using var h = new RunHarness();
        h.PullStatus = 422;
        await Assert.ThrowsAsync<GitHubRequestException>(() => h.RunFactoryAsync(Ct));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task AnExceptionFromTheTestGateStillRemovesTheContainer()
    {
        using var h = new RunHarness();
        h.TestCommandThrows = true;
        await Assert.ThrowsAsync<CommandStartException>(() => h.RunFactoryAsync(Ct));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task AFailedCloneThrowsAndStartsNoContainer()
    {
        using var h = new RunHarness();
        h.CloneFails = true;
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunFactoryAsync(Ct));
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task APassingRunRemovesTheContainerAfterTheDelivery()
    {
        using var h = new RunHarness();
        await h.RunFactoryAsync(Ct);
        Assert.Equal(["push", "comment", "rm"], h.Events.Where(e => e is "push" or "comment" or "rm"));
    }

    [Fact]
    public async Task TheSecretNamesReachOnlyTheIntegrationCommand()
    {
        using var h = new RunHarness();
        h.SecretNames = ["WEATHER_API_KEY"];
        await h.RunFactoryAsync(Ct);
        List<CommandSpec> withName = [.. h.Commands.Where(c => c.Arguments.Contains("WEATHER_API_KEY"))];
        CommandSpec only = Assert.Single(withName);
        Assert.Contains("tests/Integration", only.Arguments);
    }
}
