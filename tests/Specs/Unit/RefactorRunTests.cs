using System.Globalization;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class RefactorRunTests
{
    private const string Check = "test -f src/Core/Greeting.cs";

    private static FactoryRunHarness Make(string body = "## Check\n\n```sh\n" + Check + "\n```\n", params string[] labels)
    {
        var h = new FactoryRunHarness { Title = "Rename Greeting", Body = body };
        h.Labels.Add("refactor");
        h.Labels.AddRange(labels);
        return h;
    }

    private static IEnumerable<SeenPrompt> Builds(FactoryRunHarness h) => h.Prompts.Where(p => p.Kind is "build");

    private static string NextBuildText(FactoryRunHarness h) => Builds(h).ElementAt(1).Text;

    [Theory]
    [InlineData("behavior")]
    [InlineData("cleanup")]
    public async Task RefactorWithAnExcludedLabelGetsOneCommentNoCloneAndNoContainer(string other)
    {
        using FactoryRunHarness h = Make(labels: other);
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        string comment = Assert.Single(h.CommentBodies);
        Assert.Contains("refactor", comment, StringComparison.Ordinal);
        Assert.Contains(other, comment, StringComparison.Ordinal);
        Assert.Empty(h.Commands);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task ACheckThatPassesBeforeThePlanGetsACommentAndTheCopyIsRemoved()
    {
        using FactoryRunHarness h = Make();
        h.CheckResults[0] = (0, "", "");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("The check passes before the plan", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "plan");
        Assert.Contains(h.Commands, c => c.Arguments.Contains("remove"));
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task ACheckThatDoesNotRunBeforeThePlanGetsItsCodeAndStandardError(int code)
    {
        using FactoryRunHarness h = Make();
        h.CheckResults[0] = (code, "", "no such command");
        await h.RunAsync(TestContext.Current.CancellationToken);
        string comment = Assert.Single(h.CommentBodies);
        Assert.Contains(code.ToString(CultureInfo.InvariantCulture), comment, StringComparison.Ordinal);
        Assert.Contains("no such command", comment, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "plan");
    }

    [Theory]
    [InlineData("specs/features/checkout.feature")]
    [InlineData("src/Core/Extra.cs")]
    public async Task APlanThatWritesAnotherFileGetsASecondPromptNamingIt(string path)
    {
        using FactoryRunHarness h = Make();
        h.PlanExtraFiles.Add(path);
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(path, h.Prompts.Where(p => p.Kind is "plan").ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APlanThatChangesOrRemovesAnExistingFileFailsNamingIt(bool remove)
    {
        using FactoryRunHarness h = Make();
        h.AfterClone = directory =>
        {
            Directory.CreateDirectory(Path.Combine(directory, "src"));
            File.WriteAllText(Path.Combine(directory, "src/Old.cs"), "old");
        };
        (remove ? h.PlanRemoves : h.PlanExtraFiles).Add("src/Old.cs");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("src/Old.cs", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Contains(remove ? "gone" : "changed", h.CommentBodies.Single(), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "build");
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task AMissingPlanOnBothPromptsGetsOneCommentAndNoBuild()
    {
        using FactoryRunHarness h = Make();
        h.AgentWritesPlan = false;
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(".factory/plan.md", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Equal(2, h.Prompts.Count(p => p.Kind is "plan"));
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "build");
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task ARefactorPlanPromptHoldsNoFeatureDirectory()
    {
        using FactoryRunHarness h = Make();
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("specs/features/", h.Prompts.First(p => p.Kind is "plan").Text, StringComparison.Ordinal);
        Assert.DoesNotContain("tests/Specs/Unit/", Builds(h).First().Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tests/Specs/Steps/GreetingSteps.cs")]
    [InlineData("tests/Integration/Integration.csproj")]
    [InlineData("specs/features/greeting.feature")]
    public async Task AChangedPinnedFileGetsAPinRedoBeforeTheReview(string path)
    {
        using FactoryRunHarness h = Make();
        h.FirstBuildChanges.Add(path);
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains($"pin: {path}", NextBuildText(h), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts.TakeWhile(p => p != Builds(h).ElementAt(1)), p => p.Kind is "review");
    }

    [Fact]
    public async Task AnAddedUnitTestFileGetsAPinRedo()
    {
        using FactoryRunHarness h = Make();
        h.FirstBuildAdds.Add("tests/Specs/Unit/GreetingTests.cs");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("pin: tests/Specs/Unit/GreetingTests.cs", NextBuildText(h), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARemovedStepFileGetsAPinRedo()
    {
        using FactoryRunHarness h = Make();
        h.FirstBuildRemoves.Add("tests/Specs/Steps/GreetingSteps.cs");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("pin: tests/Specs/Steps/GreetingSteps.cs", NextBuildText(h), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedCheckAfterABuildGetsARedoWithItsOutputAndNoReviewBeforeIt()
    {
        using FactoryRunHarness h = Make();
        h.CheckResults[1] = (1, "the out", "the err");
        await h.RunAsync(TestContext.Current.CancellationToken);
        string text = NextBuildText(h);
        Assert.Contains("code 1", text, StringComparison.Ordinal);
        Assert.Contains("the out", text, StringComparison.Ordinal);
        Assert.Contains("the err", text, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts.TakeWhile(p => p != Builds(h).ElementAt(1)), p => p.Kind is "review");
    }

    [Fact]
    public async Task ABlockedReviewGetsARedoWithTheBlockingItemAndANextBuild()
    {
        using FactoryRunHarness h = Make();
        h.RejectedBuilds.Add(1);
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(FactoryRunHarness.Blocking, NextBuildText(h), StringComparison.Ordinal);
        Assert.Equal(1, result.Redos);
        Assert.NotNull(result.PullRequest);
    }

    [Fact]
    public async Task FourRedChecksGiveOneCommentAndNoPush()
    {
        using FactoryRunHarness h = Make();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.CheckResults[build] = (1, "", "still red");
        }

        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("still red", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "git" && c.Arguments[0] is "push");
        Assert.Equal(new FactoryRunResult(null, 3), result);
    }

    [Fact]
    public async Task AWorktreeThatCannotBeAddedStopsTheCheckWithItsStandardError()
    {
        using FactoryRunHarness h = Make();
        h.WorktreeAddFails = true;
        SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => h.RunAsync(TestContext.Current.CancellationToken));
        Assert.Contains("cannot add the worktree", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "plan");
    }

    [Theory]
    [InlineData("Rename Greeting to Greeter.")]
    [InlineData("## Check\nRun it by hand.\n")]
    public async Task ABodyWithNoCheckBlockRunsNoShellAndNoWorktreeAndStartsWithThePlan(string body)
    {
        using FactoryRunHarness h = Make(body);
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker" && c.Arguments[0] is "exec" && (c.Arguments.Contains("-c") || c.Arguments.Contains("worktree")));
        Assert.Equal("plan", h.Prompts[0].Kind);
    }

    [Fact]
    public async Task APlanPromptWithTheErrorFlagFailsWithItsText()
    {
        using FactoryRunHarness h = Make();
        h.PlanIsError = true;
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("plan went wrong", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "build");
        Assert.Null(result.PullRequest);
    }
}
