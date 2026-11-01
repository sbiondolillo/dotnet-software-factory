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
        Assert.Contains(path, result.Failures[0], StringComparison.Ordinal);
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
        Assert.Single(result.Failures, f => f.Contains("specs/features/empty.feature", StringComparison.Ordinal));
        Assert.Single(result.Failures, f => f.Contains("specs/features/none.feature", StringComparison.Ordinal));
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
        Assert.Single(result.Failures, f => f.Contains("src/deep/New.cs", StringComparison.Ordinal));
        Assert.Single(result.Failures, f => f.Contains("a/changed.txt", StringComparison.Ordinal));
        Assert.Single(result.Failures, f => f.Contains("removed.txt", StringComparison.Ordinal));
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
        Assert.Single(_h.Prompts);
    }

    [Fact]
    public async Task AnErrorOnARefactorPlanSendsOnePromptAndFails()
    {
        _h.Refactor = true;
        _h.Answer(null, true, "Invalid API key");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal(["Invalid API key"], result.Failures);
        Assert.Single(_h.Prompts);
    }

    [Fact]
    public async Task AnErrorWithNoTextSendsOnePromptAndGivesTheFailureSentence()
    {
        _h.Answer(null, true, "");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.Single(_h.Prompts);
        Assert.Equal(["claude answered with an error and no text."], result.Failures);
    }

    [Fact]
    public async Task AnErrorOnTheFirstRunFailsAfterOnePrompt()
    {
        _h.AgentWritesSoundFiles(null);
        _h.Answer(1, true, "Overloaded");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal(["Overloaded"], result.Failures);
        Assert.Single(_h.Prompts);
    }

    [Fact]
    public async Task TheSecondPromptHoldsEveryFirstFailureAndNoThirdPromptRuns()
    {
        _h.AgentWritesSoundFiles(1);
        _h.AgentWrites(1, "src/One.cs", "x");
        _h.AgentWrites(1, "src/Two.cs", "x");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.Equal(2, _h.Prompts.Count);
        Assert.Contains("src/One.cs", _h.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("src/Two.cs", _h.Prompts[1], StringComparison.Ordinal);
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
            Assert.Contains("Title T", prompt, StringComparison.Ordinal);
            Assert.Contains("Line one\nLine two", prompt, StringComparison.Ordinal);
            Assert.Contains("specs/features/", prompt, StringComparison.Ordinal);
            Assert.Contains(".factory/plan.md", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ARejectedPromptPropagates()
    {
        _h.RawOutput = "no json";
        await Assert.ThrowsAsync<ClaudeCodeException>(() => _h.RunAsync());
    }

    private const string Greeting = "specs/features/greeting.feature";

    [Fact]
    public async Task RemovingTheOnlyFeatureFileFailsOnBothRunsNamingTheDirectory()
    {
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.AgentRemoves(null, Greeting);
        _h.AgentWrites(null, ".factory/plan.md", "p");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal(2, _h.Prompts.Count);
        Assert.Contains(result.Failures, f => f.Contains("specs/features/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovalAloneWithAnotherFileRemainingPasses()
    {
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentRemoves(null, Greeting);
        _h.AgentWrites(null, ".factory/plan.md", "p");
        Assert.True((await _h.RunAsync()).Passed);
    }

    [Fact]
    public async Task RemovalLeavingAFeatureFileWithoutScenarioFailsNamingIt()
    {
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.Write("specs/features/old.feature", "Feature: Old\n");
        _h.AgentRemoves(null, Greeting);
        _h.AgentWrites(null, ".factory/plan.md", "p");
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/old.feature");
    }

    [Fact]
    public async Task RemovalLeavingAFeatureFileThatDoesNotParseFailsNamingIt()
    {
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.Write("specs/features/bad.feature", "This is not a feature file.");
        _h.AgentRemoves(null, Greeting);
        _h.AgentWrites(null, ".factory/plan.md", "p");
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/bad.feature");
    }

    [Fact]
    public async Task RemovalWithNoPlanFailsNamingThePlan()
    {
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentRemoves(null, Greeting);
        AssertOneFailureNaming(await _h.RunAsync(), ".factory/plan.md");
    }

    [Fact]
    public async Task RemovalWithAChangedFileOutsideFailsNamingIt()
    {
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentRemoves(null, Greeting);
        _h.AgentWrites(null, ".factory/plan.md", "p");
        _h.AgentWrites(null, "src/New.cs", "x");
        AssertOneFailureNaming(await _h.RunAsync(), "src/New.cs");
    }

    [Fact]
    public async Task RemovingANonFeatureFileUnderTheDirectoryIsNoFeatureChange()
    {
        _h.Write("specs/features/notes.md", "x");
        _h.Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentRemoves(null, "specs/features/notes.md");
        _h.AgentWrites(null, ".factory/plan.md", "p");
        AssertOneFailureNaming(await _h.RunAsync(), "specs/features/");
    }

    [Fact]
    public async Task TheCorrectionPromptStillHoldsTheRemovalSentenceOnce()
    {
        const string Sentence = "Remove each feature file whose behaviour the issue removes or replaces.";
        _h.Write(Greeting, PlanPhaseHarness.SoundFeature);
        _h.Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
        _h.AgentRemoves(1, Greeting);
        _h.AgentWrites(1, ".factory/plan.md", "p");
        _h.AgentWrites(1, "src/New.cs", "x");
        _h.AgentRemoves(2, "src/New.cs");
        PlanPhaseResult result = await _h.RunAsync();
        Assert.True(result.Passed);
        Assert.Equal(2, _h.Prompts.Count);
        Assert.Equal(1, _h.Prompts[0].Split(Sentence).Length - 1);
        Assert.Contains(Sentence, _h.Prompts[1], StringComparison.Ordinal);
    }

    private static string Doc(string delimiter, string indent, params string[] lines) =>
        "Feature: F\n\n  Scenario: S\n    Given a\n"
        + $"      {delimiter}\n" + string.Concat(lines.Select(l => l.Length == 0 ? "\n" : $"      {indent}{l}\n")) + $"      {delimiter}\n";

    private async Task<PlanPhaseResult> RunWithAsync(params (string Path, string Text)[] files)
    {
        _h.AgentWrites(null, ".factory/plan.md", "p");
        foreach ((string path, string text) in files)
        {
            _h.AgentWrites(null, path, text);
        }

        return await _h.RunAsync();
    }

    private static void AssertDeep(PlanPhaseResult result, string path, int line)
    {
        Assert.False(result.Passed);
        Assert.Single(result.Failures);
        Assert.Contains(path, result.Failures[0], StringComparison.Ordinal);
        Assert.Contains($"line {line}", result.Failures[0], StringComparison.Ordinal);
        Assert.Contains("the column of its opening delimiter", result.Failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeeperQuoteDocStringFails() =>
        AssertDeep(await RunWithAsync(("specs/features/a.feature", Doc("\"\"\"", "  ", "x"))), "specs/features/a.feature", 5);

    [Fact]
    public async Task ADeeperBacktickDocStringFails() =>
        AssertDeep(await RunWithAsync(("specs/features/a.feature", Doc("```", "  ", "x"))), "specs/features/a.feature", 5);

    [Fact]
    public async Task AMixedDocStringPasses()
    {
        PlanPhaseResult result = await RunWithAsync(("specs/features/a.feature", Doc("\"\"\"", "", "a", "  b")));
        Assert.True(result.Passed, string.Join("; ", result.Failures));
    }

    [Fact]
    public async Task ABlankLineDoesNotCountAsFlatText() =>
        AssertDeep(await RunWithAsync(("specs/features/a.feature", Doc("\"\"\"", "  ", "x", "", "y"))), "specs/features/a.feature", 5);

    [Fact]
    public async Task ADelimiterOfTheOtherKindInsideIsJudgedByTheOuterOne()
    {
        string text = Doc("```", "  ", "\"\"\"", "x", "\"\"\"");
        AssertDeep(await RunWithAsync(("specs/features/a.feature", text)), "specs/features/a.feature", 5);
    }

    [Fact]
    public async Task ABacktickDelimiterInsideAQuoteDocStringIsJudgedByTheOuterOne() =>
        AssertDeep(await RunWithAsync(("specs/features/a.feature", Doc("\"\"\"", "  ", "```", "x", "```"))), "specs/features/a.feature", 5);

    [Fact]
    public async Task TwoFilesGiveOneFailureEachInPathOrder()
    {
        PlanPhaseResult result = await RunWithAsync(
            ("specs/features/b.feature", Doc("\"\"\"", "  ", "x")),
            ("specs/features/a.feature", Doc("```", "  ", "x")));
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains("specs/features/a.feature", result.Failures[0], StringComparison.Ordinal);
        Assert.Contains("specs/features/b.feature", result.Failures[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACorrectionRunThatFixesTheIndentPasses()
    {
        _h.AgentWrites(null, ".factory/plan.md", "p");
        _h.AgentWrites(1, "specs/features/a.feature", Doc("\"\"\"", "  ", "x"));
        _h.AgentWrites(2, "specs/features/a.feature", Doc("\"\"\"", "", "x"));
        PlanPhaseResult result = await _h.RunAsync();
        Assert.True(result.Passed, string.Join("; ", result.Failures));
        Assert.Equal(2, _h.Prompts.Count);
    }

    [Fact]
    public async Task ACorrectionRunThatKeepsTheIndentFails()
    {
        PlanPhaseResult result = await RunWithAsync(("specs/features/a.feature", Doc("\"\"\"", "  ", "x")));
        AssertDeep(result, "specs/features/a.feature", 5);
        Assert.Null(result.Pin);
        Assert.Equal(2, _h.Prompts.Count);
    }

    [Fact]
    public async Task AnOverIndentedDocStringInAFileThatTheAgentLeavesAsItIsPasses()
    {
        _h.Write("specs/features/old.feature", Doc("\"\"\"", "  ", "x"));
        _h.AgentWritesSoundFiles(null);
        PlanPhaseResult result = await _h.RunAsync();
        Assert.True(result.Passed, string.Join("; ", result.Failures));
        Assert.Single(_h.Prompts);
    }

    [Fact]
    public async Task AnOverIndentedDocStringInAFileThatTheAgentChangesFails()
    {
        _h.Write("specs/features/old.feature", Doc("\"\"\"", "  ", "x"));
        _h.AgentWrites(null, "specs/features/old.feature", Doc("\"\"\"", "  ", "y"));
        _h.AgentWrites(null, ".factory/plan.md", "p");
        AssertDeep(await _h.RunAsync(), "specs/features/old.feature", 5);
    }

    [Fact]
    public async Task AnOverIndentedDocStringInAnUnchangedFileDoesNotHideOneInANewFile()
    {
        _h.Write("specs/features/old.feature", Doc("\"\"\"", "  ", "x"));
        PlanPhaseResult result = await RunWithAsync(("specs/features/new.feature", Doc("\"\"\"", "  ", "x")));
        AssertDeep(result, "specs/features/new.feature", 5);
    }
}
