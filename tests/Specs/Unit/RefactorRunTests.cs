using System.Globalization;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class RefactorRunTests
{
    private const string Check = "test -f src/Core/Greeting.cs";

    private static RunHarness Make(string body = "## Check\n\n```sh\n" + Check + "\n```\n", params string[] labels)
    {
        var h = new RunHarness { Title = "Rename Greeting", Body = body };
        h.Labels.Add("refactor");
        h.Labels.AddRange(labels);
        return h;
    }

    private static IEnumerable<AgentRequest> Builds(RunHarness h) => h.Builder.Of(Phase.Build);

    /// <summary>The requests before the second build prompt.</summary>
    private static IEnumerable<AgentRequest> BeforeSecondBuild(RunHarness h) => h.AgentRequests.TakeWhile(r => r != Builds(h).ElementAt(1));

    private static string NextBuildText(RunHarness h) => Builds(h).ElementAt(1).Prompt.Text;

    [Theory]
    [InlineData("behavior")]
    [InlineData("cleanup")]
    public async Task RefactorWithAnExcludedLabelGetsOneCommentNoCloneAndNoContainer(string other)
    {
        using RunHarness h = Make(labels: other);
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        string comment = Assert.Single(h.CommentBodies);
        Assert.Contains("refactor", comment, StringComparison.Ordinal);
        Assert.Contains(other, comment, StringComparison.Ordinal);
        Assert.Empty(h.Commands);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task ACheckThatPassesBeforeThePlanGetsACommentAndTheCopyIsRemoved()
    {
        using RunHarness h = Make();
        h.CheckResults[0] = (0, "", "");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("The check passes before the plan", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Plan);
        Assert.Contains(h.Commands, c => c.Arguments.Contains("remove"));
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task ACheckThatDoesNotRunBeforeThePlanGetsItsCodeAndStandardError(int code)
    {
        using RunHarness h = Make();
        h.CheckResults[0] = (code, "", "no such command");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        string comment = Assert.Single(h.CommentBodies);
        Assert.Contains(code.ToString(CultureInfo.InvariantCulture), comment, StringComparison.Ordinal);
        Assert.Contains("no such command", comment, StringComparison.Ordinal);
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Plan);
    }

    [Theory]
    [InlineData("specs/features/checkout.feature")]
    [InlineData("src/Core/Extra.cs")]
    public async Task APlanThatWritesAnotherFileGetsASecondPromptNamingIt(string path)
    {
        using RunHarness h = Make();
        h.Builder.Act(Phase.Plan, _ => h.Write(path, PlanPhaseHarness.SoundFeature), 1);
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains(path, h.Builder.Of(Phase.Plan).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APlanThatChangesOrRemovesAnExistingFileFailsNamingIt(bool remove)
    {
        using RunHarness h = Make();
        h.AfterClone = directory =>
        {
            Directory.CreateDirectory(Path.Combine(directory, "src"));
            File.WriteAllText(Path.Combine(directory, "src/Old.cs"), "old");
        };
        if (remove)
        {
            h.Builder.Act(Phase.Plan, _ => h.Remove("src/Old.cs"), 1);
        }
        else
        {
            h.Builder.Act(Phase.Plan, _ => h.Write("src/Old.cs", PlanPhaseHarness.SoundFeature), 1);
        }
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("src/Old.cs", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Contains(remove ? "gone" : "changed", h.CommentBodies.Single(), StringComparison.Ordinal);
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Build);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task AMissingPlanOnBothPromptsGetsOneCommentAndNoBuild()
    {
        using RunHarness h = Make();
        h.Builder.Clear(Phase.Plan);
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains(".factory/plan.md", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Equal(2, h.Builder.Of(Phase.Plan).Count());
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Build);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task ARefactorPlanPromptHoldsNoFeatureDirectory()
    {
        using RunHarness h = Make();
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("specs/features/", h.Builder.Of(Phase.Plan).First().Prompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("tests/Specs/Unit/", Builds(h).First().Prompt.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tests/Specs/Steps/GreetingSteps.cs")]
    [InlineData("tests/Integration/Integration.csproj")]
    [InlineData("specs/features/greeting.feature")]
    public async Task AChangedPinnedFileGetsAPinRedoBeforeTheReview(string path)
    {
        using RunHarness h = Make();
        h.FirstBuildChanges.Add(path);
        h.CloneFiles.Add(path);
        h.BuildChanges(1, path);
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains($"pin: {path}", NextBuildText(h), StringComparison.Ordinal);
        Assert.DoesNotContain(BeforeSecondBuild(h), r => r.Phase is Phase.Review);
    }

    [Fact]
    public async Task AnAddedUnitTestFileGetsAPinRedo()
    {
        using RunHarness h = Make();
        h.BuildWrites(1, "tests/Specs/Unit/GreetingTests.cs");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("pin: tests/Specs/Unit/GreetingTests.cs", NextBuildText(h), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARemovedStepFileGetsAPinRedo()
    {
        using RunHarness h = Make();
        h.CloneFiles.Add("tests/Specs/Steps/GreetingSteps.cs");
        h.BuildRemoves(1, "tests/Specs/Steps/GreetingSteps.cs");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("pin: tests/Specs/Steps/GreetingSteps.cs", NextBuildText(h), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedCheckAfterABuildGetsARedoWithItsOutputAndNoReviewBeforeIt()
    {
        using RunHarness h = Make();
        h.CheckResults[1] = (1, "the out", "the err");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        string text = NextBuildText(h);
        Assert.Contains("code 1", text, StringComparison.Ordinal);
        Assert.Contains("the out", text, StringComparison.Ordinal);
        Assert.Contains("the err", text, StringComparison.Ordinal);
        Assert.DoesNotContain(BeforeSecondBuild(h), r => r.Phase is Phase.Review);
    }

    [Fact]
    public async Task AWorktreeThatCannotBeAddedStopsTheCheckWithItsStandardError()
    {
        using RunHarness h = Make();
        h.WorktreeAddFails = true;
        SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => h.RunFactoryAsync(TestContext.Current.CancellationToken));
        Assert.Contains("cannot add the worktree", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Plan);
    }

    [Theory]
    [InlineData("Rename Greeting to Greeter.")]
    [InlineData("## Check\nRun it by hand.\n")]
    public async Task ABodyWithNoCheckBlockRunsNoShellAndNoWorktreeAndStartsWithThePlan(string body)
    {
        using RunHarness h = Make(body);
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker" && c.Arguments[0] is "exec" && (c.Arguments.Contains("-c") || c.Arguments.Contains("worktree")));
        Assert.Equal(Phase.Plan, h.AgentRequests[0].Phase);
    }

    [Fact]
    public async Task APlanPromptWithTheErrorFlagFailsWithItsText()
    {
        using RunHarness h = Make();
        h.Builder.Answer(Phase.Plan, FakeAgent.Error("plan went wrong"));
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("plan went wrong", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.AgentRequests, r => r.Phase is Phase.Build);
        Assert.Null(result.PullRequest);
    }
}
