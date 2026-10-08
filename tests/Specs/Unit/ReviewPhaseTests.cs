using System.Text;
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

        Assert.Contains("diff", exception.Message);
        Assert.Contains("fatal: bad object abc", exception.Message);
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
        Assert.Contains(".factory/review.json", Assert.Single(result.Blocking));
    }

    [Fact]
    public async Task AnErrorAnswerWithWhitespaceTextGivesOneNonEmptyItem()
    {
        using var h = new ReviewPhaseHarness { IsError = true, ResultText = "  \n" };

        ReviewPhaseResult result = await h.RunAsync();

        Assert.False(result.Approved);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(result.Blocking)));
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
    public async Task ALeftoverVerdictIsGoneBeforeTheAgentRuns()
    {
        using var h = new ReviewPhaseHarness();
        h.Write(".factory/review.json", "{\"approved\": true}");

        ReviewPhaseResult result = await h.RunAsync();

        Assert.Null(h.VerdictBeforeAgent);
        Assert.False(result.Approved);
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

        Assert.Contains("diff --git a/x b/x\n+line\n", h.Prompts[0]);
        Assert.DoesNotContain("[cut]", h.Prompts[0]);
    }

    [Fact]
    public async Task ASurrogatePairDiffIsCutOnWholeCharactersAndFits()
    {
        using var h = new ReviewPhaseHarness { DiffText = string.Concat(Enumerable.Repeat("\U0001F600", 60000)) };

        await h.RunAsync();

        string prompt = h.Prompts[0];
        Assert.True(Encoding.UTF8.GetByteCount(prompt) <= 131071);
        Assert.EndsWith("[cut]", prompt);
        for (int i = 0; i < prompt.Length; i++)
        {
            if (char.IsHighSurrogate(prompt[i]))
            {
                Assert.True(i + 1 < prompt.Length && char.IsLowSurrogate(prompt[i + 1]));
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(prompt[i]));
            }
        }
    }

    [Fact]
    public async Task ACutDiffKeepsItsStartAndTheIssueAndTheBase()
    {
        using var h = new ReviewPhaseHarness { DiffText = "START" + new string('a', 200000) };

        await h.RunAsync();

        string prompt = h.Prompts[0];
        Assert.Contains("START" + new string('a', 1000), prompt);
        Assert.EndsWith("[cut]", prompt);
        Assert.Contains("Add a checkout total", prompt);
        Assert.Contains("The checkout shows the total of the cart.", prompt);
        Assert.Contains(ReviewPhaseHarness.DefaultBase, prompt);
    }

    [Fact]
    public async Task ThePromptHoldsOnlyRelativePaths()
    {
        using var h = new ReviewPhaseHarness();

        await h.RunAsync();

        Assert.DoesNotContain(h.WorkTree, h.Prompts[0]);
    }
}
