using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackRefactorRunTests
{
    private const string CheckBody = "## Check\n\n```sh\ngrep -q Greeting src/Core/Greeter.cs\n```\n";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RunHarness Refactor(string label = "refactor", string body = CheckBody, string? pull = "Closes #15")
    {
        var h = new RunHarness { PullBody = pull, Body = body };
        h.Labels.Add(label);
        return h;
    }

    private static bool RunsCheck(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        int id = arguments.IndexOf(GateSandbox.ContainerId);
        return id >= 0 && arguments.Skip(id + 1).Take(2).SequenceEqual(["sh", "-c"]);
    }

    [Theory]
    [InlineData("tests/Specs/Steps/GreetingSteps.cs", false)]
    [InlineData("tests/Specs/Unit/GreetingTests.cs", true)]
    [InlineData("tests/Integration/GreetingTests.cs", false)]
    [InlineData("specs/features/greeting.feature", false)]
    public async Task AChangeUnderAPinnedDirectoryGivesAPinRedoAndNoEarlyReview(string path, bool added)
    {
        using RunHarness h = Refactor();
        h.FirstBuildChanges.Add(path);
        if (added)
        {
            h.BuildWrites(1, path);
        }
        else
        {
            h.CloneFiles.Add(path);
            h.BuildChanges(1, path);
        }

        await h.RunFeedbackAsync(Ct);
        Assert.Contains($"pin: {path}", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
        Assert.True(h.AgentRequests.IndexOf(h.Builder.Of(Phase.Build).ElementAt(1)) is 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(126)]
    [InlineData(127)]
    public async Task AFailingCheckGivesARedoAndNoReviewBeforeIt(int code)
    {
        using RunHarness h = Refactor();
        h.CheckResults[1] = (code, "", "");
        await h.RunFeedbackAsync(Ct);
        Assert.Contains($"The check command exited with the code {code}", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
        Assert.Equal(Phase.Build, h.AgentRequests[1].Phase);
    }

    [Fact]
    public async Task AnIssueWithNoCheckRunsNoCheckAndStartsTheReview()
    {
        using RunHarness h = Refactor(body: "Rename Greeting to Greeter.");
        await h.RunFeedbackAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.NotEmpty(h.Reviewer.Of(Phase.Review));
    }

    [Fact]
    public async Task ABehaviorIssueKeepsTheMainRun()
    {
        using RunHarness h = Refactor("behavior");
        h.FirstBuildChanges.Add("tests/Specs/Steps/GreetingSteps.cs");
        await h.RunFeedbackAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.DoesNotContain("pin:", h.Builder.Of(Phase.Build).Last().Prompt.Text, StringComparison.Ordinal);
        Assert.NotEmpty(h.Reviewer.Of(Phase.Review));
    }

    [Fact]
    public async Task ABodyWithNoClosingLineReadsNoIssue()
    {
        using RunHarness h = Refactor(pull: "Answers the review.");
        h.FirstBuildChanges.Add("tests/Specs/Steps/GreetingSteps.cs");
        await h.RunFeedbackAsync(Ct);
        Assert.DoesNotContain(h.Requests, r => r.Uri.Contains("/issues/", StringComparison.Ordinal));
        Assert.DoesNotContain("pin:", h.Builder.Of(Phase.Build).Last().Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRefactorPromptsNameTheIntegrationTestsAndNotThePlan()
    {
        using RunHarness h = Refactor();
        await h.RunFeedbackAsync(Ct);
        foreach (AgentRequest prompt in h.AgentRequests)
        {
            Assert.Contains("tests/Integration/", prompt.Prompt.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(".factory/plan.md", prompt.Prompt.Text, StringComparison.Ordinal);
        }
    }
}
