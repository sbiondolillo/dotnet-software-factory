using Core;
using Xunit;

namespace Specs.Unit;

public sealed class ErrorAnswerStopsRunTests
{
    private const string NoText = "claude answered with an error and no text.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FactoryRunHarness Make(string? label)
    {
        var h = new FactoryRunHarness();
        switch (label)
        {
            case "cleanup":
                h.Labels.Add("cleanup");
                h.Title = "Fix the install test";
                h.Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ngrep -q Install README.md\n```\n";
                h.FirstBuildChanges.Add("README.md");
                break;
            case "refactor":
                h.Labels.Add("refactor");
                h.Title = "Rename Greeting";
                h.Body = "## Check\n\n```sh\ntest -f src/Core/Greeting.cs\n```\n";
                h.FirstBuildChanges.Add("src/Core/Greeting.cs");
                break;
        }

        return h;
    }

    private static void AssertStopped(FactoryRunHarness h, FactoryRunResult result, string text)
    {
        Assert.Equal(new FactoryRunResult(null, 0), result);
        string body = Assert.Single(h.CommentBodies);
        Assert.Contains(text, body);
        Assert.Contains(body + Environment.NewLine, h.Progress.ToString());
        Assert.DoesNotContain("push", h.Events);
        Assert.Equal("rm", h.Events[^1]);
        Assert.Equal(h.Prompts.Count, h.Commands.Count(c => c.Command is "docker" && c.Arguments[0] is "exec" && c.Arguments.Contains("claude")));
    }

    [Theory]
    [InlineData(null, "plan")]
    [InlineData("refactor", "plan")]
    public async Task AnErrorOnThePlanEndsTheRunAtThePlan(string? label, string last)
    {
        using FactoryRunHarness h = Make(label);
        h.PlanIsError = true;
        h.ErrorText = "Invalid API key";
        AssertStopped(h, await h.RunAsync(Ct), "Invalid API key");
        Assert.Equal(last, h.Prompts[^1].Kind);
        Assert.Single(h.Prompts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cleanup")]
    [InlineData("refactor")]
    public async Task AnErrorOnTheFirstBuildStopsBeforeTheGatesAndTheReview(string? label)
    {
        using FactoryRunHarness h = Make(label);
        h.ErrorBuilds.Add(1);
        h.ErrorText = "Invalid API key";
        AssertStopped(h, await h.RunAsync(Ct), "Invalid API key");
        Assert.Equal("build", h.Prompts[^1].Kind);
        int build = h.Commands.FindLastIndex(c => c.Arguments.Contains("claude"));
        Assert.DoesNotContain(h.Commands.Skip(build + 1), c => c.Arguments.Contains("--logger") || c.Arguments.Contains("format"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cleanup")]
    [InlineData("refactor")]
    public async Task AnErrorOnTheFirstReviewGivesNoRedoAndNoSecondBuild(string? label)
    {
        using FactoryRunHarness h = Make(label);
        h.ErrorReviews.Add(1);
        h.ErrorText = "Invalid API key";
        AssertStopped(h, await h.RunAsync(Ct), "Invalid API key");
        Assert.Equal("review", h.Prompts[^1].Kind);
        Assert.Equal(1, h.Prompts.Count(p => p.Kind is "build"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cleanup")]
    [InlineData("refactor")]
    public async Task AnErrorOnALaterBuildStopsAtOnce(string? label)
    {
        using FactoryRunHarness h = Make(label);
        h.RedBuilds.Add(1);
        h.ErrorBuilds.Add(2);
        h.ErrorText = "Overloaded";
        AssertStopped(h, await h.RunAsync(Ct), "Overloaded");
        Assert.Equal(2, h.Prompts.Count(p => p.Kind is "build"));
        Assert.Equal("build", h.Prompts[^1].Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cleanup")]
    [InlineData("refactor")]
    public async Task AnErrorOnASecondReviewStopsAtOnce(string? label)
    {
        using FactoryRunHarness h = Make(label);
        h.RejectedBuilds.Add(1);
        h.ErrorReviews.Add(2);
        h.ErrorText = "Overloaded";
        AssertStopped(h, await h.RunAsync(Ct), "Overloaded");
        Assert.Equal(2, h.Prompts.Count(p => p.Kind is "review"));
        Assert.Equal("review", h.Prompts[^1].Kind);
    }

    [Theory]
    [InlineData(null, "plan")]
    [InlineData(null, "build")]
    [InlineData(null, "review")]
    [InlineData("cleanup", "build")]
    [InlineData("cleanup", "review")]
    [InlineData("refactor", "plan")]
    [InlineData("refactor", "build")]
    [InlineData("refactor", "review")]
    public async Task AnErrorWithEmptyOrBlankTextGetsTheFixedSentence(string? label, string stop)
    {
        foreach (string text in new[] { "", "  \n " })
        {
            using FactoryRunHarness h = Make(label);
            h.ErrorText = text;
            switch (stop)
            {
                case "plan":
                    h.PlanIsError = true;
                    break;
                case "build":
                    h.ErrorBuilds.Add(1);
                    break;
                default:
                    h.ErrorReviews.Add(1);
                    break;
            }

            AssertStopped(h, await h.RunAsync(Ct), NoText);
        }
    }
}
