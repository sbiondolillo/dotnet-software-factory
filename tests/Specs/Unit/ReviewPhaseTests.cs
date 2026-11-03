using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class ReviewPhaseTests
{
    [Fact]
    public async Task ReadDiffRunsGitDiffWithTheBaseAsOneArgumentAndReturnsTheOutputUnchanged()
    {
        CommandSpec? seen = null;
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                seen = spec;
                return Task.FromResult(new CommandResult { StandardOutput = "  text\n\n" });
            });

        string diff = await new Git(mock.Object).ReadDiffAsync("abc", "/work", TestContext.Current.CancellationToken);

        Assert.Equal("  text\n\n", diff);
        Assert.Equal("git", seen!.Command);
        Assert.Equal(["diff", "abc"], seen.Arguments);
        Assert.Equal("/work", seen.WorkingDirectory);
    }

    [Fact]
    public async Task ReadDiffGivesTheEmptyTextForEmptyOutput()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult());

        Assert.Equal("", await new Git(mock.Object).ReadDiffAsync("abc", "/work", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadDiffThrowsAGitCommandExceptionNamingDiffWhenGitFails()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 128, StandardError = "fatal: bad object abc" });

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => new Git(mock.Object).ReadDiffAsync("abc", "/work", TestContext.Current.CancellationToken));

        Assert.Contains("diff", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal: bad object abc", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGitFailureReachesTheCallerAndNoPromptRuns()
    {
        using var h = new ReviewPhaseHarness { DiffExitCode = 128 };

        await Assert.ThrowsAsync<GitCommandException>(() => h.RunAsync());

        Assert.Empty(h.Prompts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json {")]
    [InlineData("{\"approved\": false}")]
    [InlineData("{\"approved\": false, \"blocking\": null}")]
    [InlineData("{\"approved\": null, \"blocking\": []}")]
    [InlineData("{\"approved\": true, \"blocking\": [null]}")]
    public async Task AFileNotOfTheFormIsARejectionNamingTheFile(string verdict)
    {
        using var h = new ReviewPhaseHarness { VerdictToWrite = verdict };

        ReviewPhaseResult result = await h.RunAsync();

        Assert.False(result.Approved);
        Assert.Contains(".factory/review.json", Assert.Single(result.Blocking), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErrorAnswerBeatsAnApprovalInTheFile()
    {
        using var h = new ReviewPhaseHarness { IsError = true, ResultText = "Invalid API key", VerdictToWrite = "{\"approved\": true}" };
        h.Write(".factory/review.json", "{\"approved\": true}");

        ReviewPhaseResult result = await h.RunAsync();

        Assert.False(result.Approved);
        Assert.Equal("Invalid API key", Assert.Single(result.Blocking));
    }

    [Fact]
    public async Task ThePromptIsAReviewRequestThatAsksForTheVerdict()
    {
        using var h = new ReviewPhaseHarness { VerdictToWrite = "{\"approved\": true}" };

        ReviewPhaseResult result = await h.RunAsync(model: "m", turns: 5);

        AgentRequest request = Assert.Single(h.Requests);
        Assert.Equal((Phase.Review, "m", 5), (request.Phase, request.Model, request.MaxTurns));
        Assert.Equal(["review.json"], request.Outputs);
        Assert.True(result.Approved);
    }

    [Fact]
    public async Task TheVerdictComesFromTheOutputsOfTheAnswerAndNotFromTheDisk()
    {
        using var h = new ReviewPhaseHarness { VerdictToWrite = "{\"approved\": false, \"blocking\": [\"on disk\"]}" };
        h.Agent.ReadsDisk = false;
        h.Agent.Answer(FakeAgent.Done() with { Outputs = new Dictionary<string, string> { ["review.json"] = "{\"approved\": true}" } }, 1);

        ReviewPhaseResult result = await h.RunAsync();

        Assert.True(result.Approved);
    }

    [Fact]
    public async Task AVerdictFileThatThePromptDidNotWriteIsAbsent()
    {
        using var h = new ReviewPhaseHarness();
        h.Write(".factory/review.json", "{\"approved\": true}");

        ReviewPhaseResult result = await h.RunAsync();

        Assert.False(result.Approved);
        Assert.Equal(
            ".factory/review.json must hold one JSON object with approved (true or false) and blocking (a list of texts): the file is absent.",
            Assert.Single(result.Blocking));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task AVerdictThatIsNotOneJsonObjectIsARejectionThatSaysSo(string text)
    {
        using var h = new ReviewPhaseHarness { VerdictToWrite = text };

        ReviewPhaseResult result = await h.RunAsync();

        Assert.False(result.Approved);
        Assert.Equal(
            ".factory/review.json must hold one JSON object with approved (true or false) and blocking (a list of texts): the text is not one JSON object.",
            Assert.Single(result.Blocking));
    }

    [Fact]
    public async Task ATreeWithNoFactoryDirectoryIsHandled()
    {
        using var h = new ReviewPhaseHarness();

        ReviewPhaseResult result = await h.RunAsync();

        Assert.False(result.Approved);
    }

    [Fact]
    public async Task SeveralBlockingItemsKeepTheirOrder()
    {
        using var h = new ReviewPhaseHarness { VerdictToWrite = "{\"approved\": false, \"blocking\": [\"b\", \"a\", \"c\"]}" };

        ReviewPhaseResult result = await h.RunAsync();

        Assert.Equal(["b", "a", "c"], result.Blocking);
    }

    [Fact]
    public async Task ADiffThatFitsHoldsWholeWithNoCutMarker()
    {
        using var h = new ReviewPhaseHarness { DiffText = "diff --git a/x b/x\n+line\n" };

        await h.RunAsync();

        Assert.Contains("diff --git a/x b/x\n+line\n", h.Prompts[0], StringComparison.Ordinal);
        Assert.DoesNotContain("[cut]", h.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDiffIsTheOnePieceOfThePromptAndTheIssueAndTheBaseAreFixed()
    {
        string diff = "START" + new string('a', 200000);
        using var h = new ReviewPhaseHarness { DiffText = diff };

        await h.RunAsync();

        AgentPrompt prompt = Assert.Single(h.Requests).Prompt;
        Assert.Equal([diff], prompt.Pieces);
        Assert.EndsWith(diff, prompt.Text, StringComparison.Ordinal);
        string cut = prompt.Compose(["PIECE"]);
        Assert.EndsWith("PIECE", cut, StringComparison.Ordinal);
        Assert.Contains("Add a checkout total", cut, StringComparison.Ordinal);
        Assert.Contains("The checkout shows the total of the cart.", cut, StringComparison.Ordinal);
        Assert.Contains(ReviewPhaseHarness.DefaultBase, cut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePromptHoldsOnlyRelativePaths()
    {
        using var h = new ReviewPhaseHarness();

        await h.RunAsync();

        Assert.DoesNotContain(h.WorkTree, h.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePromptPutsTheJobStepsAfterYourJobAndBeforeTheVerdictStep()
    {
        using var h = new ReviewPhaseHarness { JobSteps = "JOB STEPS\n", DiffText = "DIFF" };

        await h.RunAsync(title: "TITLE", body: "BODY");

        Assert.Equal(
            "Review the build of the issue below.\n\nIssue title:\nTITLE\n\nIssue body:\nBODY\n\n"
                + $"Base commit: {ReviewPhaseHarness.DefaultBase}\n"
                + "Read every change since the base commit in the work tree.\n\n"
                + "Your job:\nJOB STEPS\n"
                + "3. Write the verdict to .factory/review.json, as one JSON object with approved and blocking. "
                + "approved is true or false; blocking is a list of texts. Approve only a build with no blocking item. "
                + "Write each blocking item as one fault, in words that a builder can act on with the item alone.\n"
                + "4. Leave every other file as it is.\n\n"
                + "The diff of the work tree against the base commit:\nDIFF",
            Assert.Single(h.Prompts));
    }
}
