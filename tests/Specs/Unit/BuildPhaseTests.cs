using Core;
using Xunit;

namespace Specs.Unit;

public sealed class BuildPhaseTests
{
    [Fact]
    public async Task AFailedAnswerHoldsItsText()
    {
        using var h = new BuildPhaseHarness { IsError = true, ResultText = "Invalid API key" };
        BuildPhaseResult result = await h.RunAsync();
        Assert.False(result.Passed);
        Assert.Equal("Invalid API key", result.Failure);
    }

    [Fact]
    public async Task ThePromptIsABuildRequestThatNamesTheOutputsAndTheResultCarriesTheOutputsAndTheFaults()
    {
        using var h = new BuildPhaseHarness { Answer = FakeAgent.Done() with { Faults = ["no commit"] } };
        h.Agent.Stage("threads.json", "{}");
        BuildPhaseResult result = await h.RunAsync(turns: 9, outputs: ["threads.json"]);
        AgentRequest request = Assert.Single(h.Requests);
        Assert.Equal((Phase.Build, 9), (request.Phase, request.MaxTurns));
        Assert.Equal(["threads.json"], request.Outputs);
        Assert.True(result.Passed);
        Assert.Equal(new Dictionary<string, string> { ["threads.json"] = "{}" }, result.Outputs);
        Assert.Equal(["no commit"], result.Faults);
    }

    [Fact]
    public async Task TheRedoTextsAreThePiecesOfThePromptAndTheIssueIsFixed()
    {
        using var h = new BuildPhaseHarness();
        string longText = new('a', 200000);
        await h.RunAsync(body: "the body", redo: [longText, "short fault"]);
        AgentPrompt prompt = Assert.Single(h.Requests).Prompt;
        Assert.Equal([longText, "short fault"], prompt.Pieces);
        string text = prompt.Text;
        Assert.Contains(longText, text, StringComparison.Ordinal);
        Assert.Contains("the body", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[cut]", text, StringComparison.Ordinal);
        Assert.Contains("the body", prompt.Compose(["x", "y"]), StringComparison.Ordinal);
        Assert.Contains("\nRedo text:\nx\n", prompt.Compose(["x", "y"]), StringComparison.Ordinal);
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
        using var h = new BuildPhaseHarness();
        h.Agent.Throw(new ClaudeCodeException("no JSON object"), 1);
        await Assert.ThrowsAsync<ClaudeCodeException>(() => h.RunAsync());
    }

    [Fact]
    public async Task ThePromptWrapsTheIssueAndEachRedoTextAroundTheLeadAndTheInstructions()
    {
        using var h = new BuildPhaseHarness { Text = new BuildText("LEAD", "INSTRUCTIONS") };
        await h.RunAsync(title: "TITLE", body: "BODY", redo: ["FIRST", "SECOND"]);
        Assert.Equal(
            "LEAD\n\nIssue title:\nTITLE\n\nIssue body:\nBODY\n\nINSTRUCTIONS"
                + "\n\nThe work tree holds the earlier build. Each text below is a fault to correct in that build.\n"
                + "\nRedo text:\nFIRST\n"
                + "\nRedo text:\nSECOND\n",
            Assert.Single(h.Prompts));
    }

    [Fact]
    public async Task APromptWithNoRedoTextEndsWithTheInstructions()
    {
        using var h = new BuildPhaseHarness { Text = new BuildText("LEAD", "INSTRUCTIONS") };
        await h.RunAsync(title: "TITLE", body: "BODY");
        Assert.Equal("LEAD\n\nIssue title:\nTITLE\n\nIssue body:\nBODY\n\nINSTRUCTIONS", Assert.Single(h.Prompts));
    }
}
