using Core;
using Xunit;

namespace Specs.Unit;

public sealed class PlanPhaseTests : IDisposable
{
    private readonly PlanPhaseHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static void AssertOneFailureNaming(PlanPhaseResult result, string path)
    {
        Assert.False(result.Passed);
        Assert.Single(result.Failures);
        Assert.Contains(path, result.Failures[0]);
    }

    [Fact]
    public async Task NoFeatureFileFailsNamingTheDirectory()
    {
        _h.AgentWrites(null, ".factory/plan.md", "p");
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/");
    }

    [Fact]
    public async Task AnUnchangedFeatureFileAndARewriteOfTheSameBytesFail()
    {
        _h.Write("specs/features/a.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentWrites(null, "specs/features/a.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentWrites(null, ".factory/plan.md", "p");
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/");
    }

    [Fact]
    public async Task AMissingPlanFailsNamingIt()
    {
        _h.AgentWrites(null, "specs/features/a.feature", PlanPhaseHarness.SoundFeature);
        AssertOneFailureNaming(await _h.RunAsync(), ".factory/plan.md");
    }

    [Fact]
    public async Task ANestedFeatureFileThatDoesNotParseFailsNamingItsPath()
    {
        _h.AgentWritesSoundFiles(null);
        _h.AgentWrites(null, "specs/features/sub/bad.feature", "This is not a feature file.");
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/sub/bad.feature");
    }

    [Fact]
    public async Task EmptyAndScenarioLessFeatureFilesFailOneEach()
    {
        _h.AgentWritesSoundFiles(null);
        _h.AgentWrites(null, "specs/features/empty.feature", "");
        _h.AgentWrites(null, "specs/features/none.feature", "Feature: None\n");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal(2, result.Failures.Count);
        Assert.Single(result.Failures, f => f.Contains("specs/features/empty.feature"));
        Assert.Single(result.Failures, f => f.Contains("specs/features/none.feature"));
    }

    [Fact]
    public async Task AnUnchangedPreExistingFeatureFileWithoutScenarioStillFails()
    {
        _h.Write("specs/features/old.feature", "Feature: Old\n");
        _h.AgentWritesSoundFiles(null);
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/old.feature");
    }

    [Fact]
    public async Task NewChangedAndRemovedFilesOutsideFailOneEach()
    {
        _h.Write("a/changed.txt", "x");
        _h.Write("removed.txt", "x");
        _h.AgentWritesSoundFiles(null);
        _h.AgentWrites(null, "src/deep/New.cs", "x");
        _h.AgentChangesOneByte(null, "a/changed.txt");
        _h.AgentRemoves(null, "removed.txt");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal(3, result.Failures.Count);
        Assert.Single(result.Failures, f => f.Contains("src/deep/New.cs"));
        Assert.Single(result.Failures, f => f.Contains("a/changed.txt"));
        Assert.Single(result.Failures, f => f.Contains("removed.txt"));
    }

    [Fact]
    public async Task IgnoredDirectoriesGiveNoFailure()
    {
        _h.AgentWritesSoundFiles(null);
        _h.AgentWrites(null, ".git/x/index", "x");
        _h.AgentWrites(null, "a/bin/x.dll", "x");
        _h.AgentWrites(null, "a/b/obj/x.json", "x");
        _h.AgentWrites(null, ".factory/notes.md", "x");
        Assert.True((await _h.RunAsync()).Passed);
    }

    [Fact]
    public async Task ErrorAnswersOnBothRunsFailWithTheTextAndSkipTheGate()
    {
        _h.AgentWritesSoundFiles(null);
        _h.Answer(null, true, "Invalid API key");
        PlanPhaseResult result = await _h.RunAsync();
        AssertOneFailureNaming(result, "Invalid API key");
        Assert.Equal(2, _h.Prompts.Count);
        Assert.Contains("Invalid API key", _h.Prompts[1]);
    }

    [Fact]
    public async Task AnErrorWithNoTextLeadsToASecondPromptAndAFailureSentence()
    {
        _h.Answer(null, true, "");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.Equal(2, _h.Prompts.Count);
        Assert.Single(result.Failures);
        Assert.False(string.IsNullOrWhiteSpace(result.Failures[0]));
    }

    [Fact]
    public async Task AnErrorOnTheFirstRunAndACleanSoundSecondRunPass()
    {
        _h.AgentWritesSoundFiles(null);
        _h.Answer(1, true, "Overloaded");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.True(result.Passed);
        Assert.Equal(2, _h.Prompts.Count);
    }

    [Fact]
    public async Task TheSecondPromptHoldsEveryFirstFailureAndNoThirdPromptRuns()
    {
        _h.AgentWritesSoundFiles(1);
        _h.AgentWrites(1, "src/One.cs", "x");
        _h.AgentWrites(1, "src/Two.cs", "x");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.Equal(2, _h.Prompts.Count);
        Assert.Contains("src/One.cs", _h.Prompts[1]);
        Assert.Contains("src/Two.cs", _h.Prompts[1]);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public async Task ASecondRunThatFixesOneBreakAndAddsAnotherHoldsOnlyTheSecondFailure()
    {
        _h.AgentWritesSoundFiles(1);
        _h.AgentWrites(1, "src/One.cs", "x");
        _h.AgentRemoves(2, "src/One.cs");
        _h.AgentWrites(2, "src/Other.cs", "x");
        AssertOneFailureNaming(await _h.RunAsync(), "src/Other.cs");
    }

    [Fact]
    public async Task APassedFirstGateRunsOnePromptAndHoldsAPin()
    {
        _h.AgentWritesSoundFiles(null);
        PlanPhaseResult result = await _h.RunAsync();
        Assert.True(result.Passed);
        Assert.Single(_h.Prompts);
        Assert.Contains("specs/features/checkout.feature", result.Pin!.Hashes.Keys);
    }

    [Fact]
    public async Task BothPromptsHoldTheIssueWholeAndThePaths()
    {
        _h.AgentWrites(null, "src/One.cs", "x");
        await _h.RunAsync("Title T", "Line one\nLine two");
        Assert.Equal(2, _h.Prompts.Count);
        foreach (string prompt in _h.Prompts)
        {
            Assert.Contains("Title T", prompt);
            Assert.Contains("Line one\nLine two", prompt);
            Assert.Contains("specs/features/", prompt);
            Assert.Contains(".factory/plan.md", prompt);
        }
    }

    [Fact]
    public async Task ARejectedPromptPropagates()
    {
        _h.RawOutput = "no json";
        await Assert.ThrowsAsync<ClaudeCodeException>(() => _h.RunAsync());
    }
}
