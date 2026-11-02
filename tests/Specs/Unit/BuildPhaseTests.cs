using System.Text;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class BuildPhaseTests
{
    private const int Limit = 131071;

    [Fact]
    public async Task AFailedAnswerHoldsItsText()
    {
        using var h = new BuildPhaseHarness { IsError = true, ResultText = "Invalid API key" };
        BuildPhaseResult result = await h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal("Invalid API key", result.Failure);
    }

    [Fact]
    public async Task AFailedAnswerWithNoTextFailsWithEmptyText()
    {
        using var h = new BuildPhaseHarness { IsError = true, OmitResult = true };
        BuildPhaseResult result = await h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal("", result.Failure);
    }

    [Fact]
    public async Task ALongRedoTextIsCutAndTheShortOneStaysWhole()
    {
        using var h = new BuildPhaseHarness();
        string longText = new('a', 200000);
        await h.RunAsync(redo: [longText, "short fault"]);
        string prompt = h.Prompts[0];
        Assert.True(Encoding.UTF8.GetByteCount(prompt) <= Limit);
        Assert.Contains("short fault", prompt, StringComparison.Ordinal);
        Assert.Contains("[cut]", prompt, StringComparison.Ordinal);
        Assert.Contains(new string('a', 100000) + "[cut]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMultibyteRedoTextIsCutOnWholeCharacters()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync(redo: [new string('é', 100000)]);
        string prompt = h.Prompts[0];
        Assert.True(Encoding.UTF8.GetByteCount(prompt) <= Limit);
        Assert.DoesNotContain('\uFFFD', prompt);
        Assert.Contains("é[cut]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoLongRedoTextsAreBothCutEvenly()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync(redo: [new string('a', 200000), new string('b', 150000)]);
        string prompt = h.Prompts[0];
        Assert.True(Encoding.UTF8.GetByteCount(prompt) <= Limit);
        Assert.Contains("The checkout shows the total of the cart.", prompt, StringComparison.Ordinal);
        int a = System.Text.RegularExpressions.Regex.Match(prompt, "a{1000,}").Length;
        int b = System.Text.RegularExpressions.Regex.Match(prompt, "b{1000,}").Length;
        Assert.True(Math.Abs(a - b) <= 1);
        Assert.Equal(2, prompt.Split("[cut]").Length - 1);
    }

    [Fact]
    public async Task ASurrogatePairIsNeverSplit()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync(redo: [string.Concat(Enumerable.Repeat("\U0001F600", 100000))]);
        string prompt = h.Prompts[0];
        Assert.True(Encoding.UTF8.GetByteCount(prompt) <= Limit);
        Assert.DoesNotContain('\uFFFD', prompt);
        Assert.Contains("\U0001F600[cut]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APromptAtTheLimitIsNotCut()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync(redo: ["x"]);
        int baseBytes = Encoding.UTF8.GetByteCount(h.Prompts[0]);
        using var exact = new BuildPhaseHarness();
        await exact.RunAsync(redo: [new string('x', Limit - baseBytes + 1)]);
        Assert.Equal(Limit, Encoding.UTF8.GetByteCount(exact.Prompts[0]));
        Assert.DoesNotContain("[cut]", exact.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyRedoListHasNoRedoSection()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync();
        Assert.DoesNotContain("[cut]", h.Prompts[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Redo", h.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIssueTooLongToFitThrowsAndSendsNoPrompt()
    {
        using var h = new BuildPhaseHarness();
        await Assert.ThrowsAsync<ClaudeCodeException>(() => h.RunAsync(body: new string('b', 200000), redo: ["short"]));
        Assert.Empty(h.Prompts);
    }

    [Fact]
    public async Task ThePromptNamesEverySettingsFile()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync();
        foreach (string name in Pin.SettingsFileNames)
        {
            Assert.Contains(name, h.Prompts[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AClaudeCodeExceptionReachesTheCaller()
    {
        using var h = new BuildPhaseHarness { RawOutput = "not json" };
        await Assert.ThrowsAsync<ClaudeCodeException>(() => h.RunAsync());
    }

    [Fact]
    public async Task ANoScenarioBuildAsksForTheCodeAloneAndLeavesTheTests()
    {
        using var h = new BuildPhaseHarness { NoScenario = true };
        await h.RunAsync();
        string prompt = Assert.Single(h.Prompts);
        Assert.Contains(BuildPhase.NoScenarioText, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Bind every scenario", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Write unit tests", prompt, StringComparison.Ordinal);
        Assert.Contains("Run dotnet build and dotnet test, and work until both pass.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABehaviourBuildAsksForBindingsAndUnitTests()
    {
        using var h = new BuildPhaseHarness();
        await h.RunAsync();
        string prompt = Assert.Single(h.Prompts);
        Assert.DoesNotContain(BuildPhase.NoScenarioText, prompt, StringComparison.Ordinal);
        Assert.Contains("Bind every scenario of every feature file", prompt, StringComparison.Ordinal);
        Assert.Contains("Write unit tests for the new code under tests/Specs/Unit/.", prompt, StringComparison.Ordinal);
    }
}
