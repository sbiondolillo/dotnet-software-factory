using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackRefactorRunTests
{
    private const string CheckBody = "## Check\n\n```sh\ngrep -q Greeting src/Core/Greeter.cs\n```\n";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FeedbackRunHarness Refactor(string label = "refactor", string body = CheckBody, string? pull = "Closes #15")
    {
        var h = new FeedbackRunHarness { PullBody = pull, IssueBody = body };
        h.Labels.Add(label);
        return h;
    }

    private static bool RunsCheck(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        int id = arguments.IndexOf(FeedbackRunHarness.ContainerId);
        return id >= 0 && arguments.Skip(id + 1).Take(2).SequenceEqual(["sh", "-c"]);
    }

    [Theory]
    [InlineData("tests/Specs/Steps/GreetingSteps.cs", false)]
    [InlineData("tests/Specs/Unit/GreetingTests.cs", true)]
    [InlineData("tests/Integration/GreetingTests.cs", false)]
    [InlineData("specs/features/greeting.feature", false)]
    public async Task AChangeUnderAPinnedDirectoryGivesAPinRedoAndNoEarlyReview(string path, bool added)
    {
        using FeedbackRunHarness h = Refactor();
        h.FirstBuildChanges.Add(path);
        if (added)
        {
            h.FirstBuildAdds.Add(path);
        }

        await h.RunAsync(Ct);
        Assert.Contains($"pin: {path}", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
        Assert.True(h.Prompts.IndexOf(h.BuildPrompts.ElementAt(1)) is 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(126)]
    [InlineData(127)]
    public async Task AFailingCheckGivesARedoAndNoReviewBeforeIt(int code)
    {
        using FeedbackRunHarness h = Refactor();
        h.CheckResults[1] = (code, "", "");
        await h.RunAsync(Ct);
        Assert.Contains($"The check command exited with the code {code}", h.BuildPrompts.ElementAt(1).Text, StringComparison.Ordinal);
        Assert.Equal("build", h.Prompts[1].Kind);
    }

    [Fact]
    public async Task AnIssueWithNoCheckRunsNoCheckAndStartsTheReview()
    {
        using FeedbackRunHarness h = Refactor(body: "Rename Greeting to Greeter.");
        await h.RunAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.Contains(h.Prompts, p => p.Kind is "review");
    }

    [Fact]
    public async Task ABehaviorIssueKeepsTheMainRun()
    {
        using FeedbackRunHarness h = Refactor("behavior");
        h.FirstBuildChanges.Add("tests/Specs/Steps/GreetingSteps.cs");
        await h.RunAsync(Ct);
        Assert.DoesNotContain(h.Commands, RunsCheck);
        Assert.DoesNotContain("pin:", h.BuildPrompts.Last().Text, StringComparison.Ordinal);
        Assert.Contains(h.Prompts, p => p.Kind is "review");
    }

    [Fact]
    public async Task ABodyWithNoClosingLineReadsNoIssue()
    {
        using FeedbackRunHarness h = Refactor(pull: "Answers the review.");
        h.FirstBuildChanges.Add("tests/Specs/Steps/GreetingSteps.cs");
        await h.RunAsync(Ct);
        Assert.DoesNotContain(h.Requests, r => r.Uri.Contains("/issues/", StringComparison.Ordinal));
        Assert.DoesNotContain("pin:", h.BuildPrompts.Last().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRefactorPromptsNameTheIntegrationTestsAndNotThePlan()
    {
        using FeedbackRunHarness h = Refactor();
        await h.RunAsync(Ct);
        foreach (SeenPrompt prompt in h.Prompts)
        {
            Assert.Contains("tests/Integration/", prompt.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(".factory/plan.md", prompt.Text, StringComparison.Ordinal);
        }
    }
}
