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
        using var h = new FactoryRunHarness();
        h.Lacks.Add(path);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Contains(path, Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Equal(new FactoryRunResult(null, 0), result);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
        Assert.Empty(h.Prompts);
    }

    [Fact]
    public async Task TwoAbsentPathsGetOneCommentNamingBoth()
    {
        using var h = new FactoryRunHarness();
        h.Lacks.Add("Solution.slnx");
        h.Lacks.Add("tests/Specs/");
        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains("Solution.slnx", body, StringComparison.Ordinal);
        Assert.Contains("tests/Specs/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathOfTheWrongKindCountsAsAbsent()
    {
        using var h = new FactoryRunHarness();
        h.Lacks.Add("Solution.slnx");
        h.Lacks.Add("specs/features/");
        h.AfterClone = directory =>
        {
            Directory.CreateDirectory(Path.Combine(directory, "Solution.slnx"));
            Directory.CreateDirectory(Path.Combine(directory, "specs"));
            File.WriteAllText(Path.Combine(directory, "specs", "features"), "");
        };
        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains("Solution.slnx", body, StringComparison.Ordinal);
        Assert.Contains("specs/features/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLayoutCheckRunsNoCommand()
    {
        using var h = new FactoryRunHarness();
        h.Lacks.Add("Solution.slnx");
        await h.RunAsync(Ct);
        Assert.Equal(["clone", "rev-parse"], h.Commands.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task AFailedPlanPhaseCommentsRunsNoBuildAndRemovesTheContainer()
    {
        using var h = new FactoryRunHarness();
        h.AgentWritesPlan = false;
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Contains(".factory/plan.md", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "build");
        Assert.Equal(new FactoryRunResult(null, 0), result);
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task FourRedGatesEndTheRunWithOneCommentAndNoPush()
    {
        using var h = new FactoryRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Equal(4, h.Prompts.Count(p => p.Kind is "build"));
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "review");
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains(FactoryRunHarness.RedText, body, StringComparison.Ordinal);
        Assert.Contains("spent", body, StringComparison.Ordinal);
        Assert.DoesNotContain("push", h.Events);
        Assert.Equal(new FactoryRunResult(null, 3), result);
    }

    [Fact]
    public async Task AnErrorOnTheSecondReviewStopsWithOneRedo()
    {
        using var h = new FactoryRunHarness();
        h.RejectedBuilds.Add(1);
        h.ErrorReviews.Add(2);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Null(result.PullRequest);
        Assert.Equal(1, result.Redos);
        Assert.Equal(2, h.Builds);
        Assert.Equal(2, h.Prompts.Count(p => p.Kind is "review"));
    }

    [Fact]
    public async Task AnErrorOnThePlainPlanPromptPostsItsTextAndRunsNoBuild()
    {
        using var h = new FactoryRunHarness();
        h.PlanIsError = true;
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Equal(new FactoryRunResult(null, 0), result);
        Assert.Equal(["plan"], h.Prompts.Select(p => p.Kind));
        Assert.Contains("plan went wrong", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErrorOnTheRefactorPlanPromptSendsOnePrompt()
    {
        using var h = new FactoryRunHarness();
        h.Labels.Add("refactor");
        h.PlanIsError = true;
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Null(result.PullRequest);
        Assert.Equal(["plan"], h.Prompts.Select(p => p.Kind));
    }

    [Fact]
    public async Task AnOverLimitCommentIsCutByBytesAndKeepsTwoHundredThousandLetters()
    {
        using var h = new FactoryRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        h.TestStdout = new string('a', 300000);
        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 262144);
        Assert.Contains(new string('a', 200000), body, StringComparison.Ordinal);
        Assert.Contains("[cut]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMultiByteCommentIsCutOnWholeCharacters()
    {
        using var h = new FactoryRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        h.TestStdout = new string('é', 200000);
        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 262144);
        Assert.DoesNotContain('\uFFFD', body);
        Assert.Contains("[cut]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommentUnderTheLimitIsPostedWhole()
    {
        using var h = new FactoryRunHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds.Add(build);
        }

        h.TestStdout = new string('a', 1000);
        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains(new string('a', 1000), body, StringComparison.Ordinal);
        Assert.DoesNotContain("[cut]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedPullRequestThrowsAndRemovesTheContainer()
    {
        using var h = new FactoryRunHarness();
        h.PullStatus = 422;
        await Assert.ThrowsAsync<GitHubRequestException>(() => h.RunAsync(Ct));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task AnExceptionFromTheTestGateStillRemovesTheContainer()
    {
        using var h = new FactoryRunHarness();
        h.TestCommandThrows = true;
        await Assert.ThrowsAsync<CommandStartException>(() => h.RunAsync(Ct));
        Assert.Contains("rm", h.Events);
    }

    [Fact]
    public async Task AFailedCloneThrowsAndStartsNoContainer()
    {
        using var h = new FactoryRunHarness();
        h.CloneFails = true;
        await Assert.ThrowsAsync<GitCommandException>(() => h.RunAsync(Ct));
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
    }

    [Fact]
    public async Task APassingRunRemovesTheContainerAfterTheDelivery()
    {
        using var h = new FactoryRunHarness();
        await h.RunAsync(Ct);
        Assert.Equal(["push", "comment", "rm"], h.Events);
    }

    [Fact]
    public async Task TheSecretNamesReachOnlyTheIntegrationCommand()
    {
        using var h = new FactoryRunHarness();
        h.SecretNames = ["WEATHER_API_KEY"];
        await h.RunAsync(Ct);
        List<CommandSpec> withName = [.. h.Commands.Where(c => c.Arguments.Contains("WEATHER_API_KEY"))];
        CommandSpec only = Assert.Single(withName);
        Assert.Contains("tests/Integration", only.Arguments);
    }
}
