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
    public async Task ARedGateSendsNoReviewAndFeedsTheEvidenceToTheNextBuild()
    {
        using var h = new FactoryRunHarness();
        h.RedBuilds.Add(1);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Equal(1, result.Redos);
        List<SeenPrompt> prompts = h.Prompts;
        Assert.Equal(["plan", "build", "build", "review"], prompts.Select(p => p.Kind));
        Assert.Contains(FactoryRunHarness.RedText, prompts[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectionFeedsItsBlockingItemsToTheNextBuildAndSpendsOneRedo()
    {
        using var h = new FactoryRunHarness();
        h.RejectedBuilds.Add(1);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Equal(1, result.Redos);
        Assert.Contains(FactoryRunHarness.Blocking, h.Prompts.Where(p => p.Kind is "build").ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedRejectRedApproveGivesThePullRequestAndThreeRedos()
    {
        using var h = new FactoryRunHarness();
        h.RedBuilds.Add(1);
        h.RejectedBuilds.Add(2);
        h.RedBuilds.Add(3);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Equal(new FactoryRunResult(new PullRequest(10, h.PullUrl), 3), result);
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
    public async Task ARejectionAfterTheFourthBuildEndsTheRunWithTheBlockingItem()
    {
        using var h = new FactoryRunHarness();
        h.RedBuilds.Add(1);
        h.RedBuilds.Add(2);
        h.RedBuilds.Add(3);
        h.RejectedBuilds.Add(4);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Contains(FactoryRunHarness.Blocking, Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Equal(4, h.Prompts.Count(p => p.Kind is "build"));
        Assert.Single(h.Prompts, p => p.Kind is "review");
        Assert.DoesNotContain("push", h.Events);
        Assert.Equal(new FactoryRunResult(null, 3), result);
    }

    [Fact]
    public async Task AnErrorFlagOnTheBuildAnswerStopsTheRunBeforeAnyGate()
    {
        using var h = new FactoryRunHarness();
        h.ErrorBuilds.Add(1);
        h.ErrorText = "Invalid API key";
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Null(result.PullRequest);
        Assert.Equal(0, result.Redos);
        Assert.Equal(["plan", "build"], h.Prompts.Select(p => p.Kind));
        Assert.DoesNotContain(h.Commands, c => c.Arguments.Contains("format"));
        Assert.DoesNotContain(h.Commands, c => c.Arguments.Contains("--logger"));
        Assert.Contains("Invalid API key", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain("push", h.Events);
        Assert.Contains("rm", h.Events);
        Assert.DoesNotContain(h.Requests, r => r.Uri.EndsWith("/pulls", StringComparison.Ordinal) && r.Method is "POST");
    }

    [Fact]
    public async Task AnErrorFlagOnTheFirstReviewAnswerSpendsNoRedo()
    {
        using var h = new FactoryRunHarness();
        h.ErrorReviews.Add(1);
        h.ErrorText = "Invalid API key";
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Null(result.PullRequest);
        Assert.Equal(0, result.Redos);
        Assert.Equal(["plan", "build", "review"], h.Prompts.Select(p => p.Kind));
        Assert.Contains("Invalid API key", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErrorOnTheSecondBuildStopsWithOneRedo()
    {
        using var h = new FactoryRunHarness();
        h.RedBuilds.Add(1);
        h.ErrorBuilds.Add(2);
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Null(result.PullRequest);
        Assert.Equal(1, result.Redos);
        Assert.Equal(2, h.Builds);
        Assert.Single(h.CommentBodies);
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

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task AnErrorWithNoTextGivesTheFixedSentenceAndStandardOutputHoldsTheBody(string text)
    {
        using var h = new FactoryRunHarness();
        h.ErrorBuilds.Add(1);
        h.ErrorText = text;
        await h.RunAsync(Ct);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains("claude answered with an error and no text.", body, StringComparison.Ordinal);
        Assert.EndsWith(body, h.Progress.ToString().TrimEnd('\n'), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cleanup", 1, 0, "build")]
    [InlineData("cleanup", 0, 1, "build,review")]
    [InlineData("refactor", 1, 0, "plan,build")]
    [InlineData("refactor", 0, 1, "plan,build,review")]
    public async Task AnErrorStopsACleanupAndARefactorRun(string label, int errorBuild, int errorReview, string kinds)
    {
        using var h = new FactoryRunHarness();
        h.Labels.Add(label);
        h.Body = "## Scope\n\nREADME.md\n\n## Check\n\n```sh\ngrep -q Install README.md\n```\n";
        if (errorBuild is 1)
        {
            h.ErrorBuilds.Add(1);
        }

        if (errorReview is 1)
        {
            h.ErrorReviews.Add(1);
        }

        h.ErrorText = "Invalid API key";
        FactoryRunResult result = await h.RunAsync(Ct);
        Assert.Null(result.PullRequest);
        Assert.Equal(0, result.Redos);
        Assert.Equal(kinds.Split(','), h.Prompts.Select(p => p.Kind));
        Assert.Contains("Invalid API key", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain("push", h.Events);
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
