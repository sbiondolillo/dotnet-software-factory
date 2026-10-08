using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CleanupRunTests
{
    private static FactoryRunHarness Make(params string[] labels)
    {
        var h = new FactoryRunHarness { Title = "Fix it", Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ntrue\n```\n" };
        h.Labels.AddRange(labels);
        return h;
    }

    [Fact]
    public async Task AnIssueWithOtherLabelsRunsThePlanPhase()
    {
        using FactoryRunHarness h = Make("factory");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(h.Prompts, p => p.Kind is "plan");
    }

    [Fact]
    public async Task BothLabelsGetOneCommentAndNoContainer()
    {
        using FactoryRunHarness h = Make("cleanup", "behavior");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("behavior", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Theory]
    [InlineData("## Check\n```sh\ntrue\n```\n", "## Scope")]
    [InlineData("## Scope\n\n## Check\n```sh\ntrue\n```\n", "## Scope")]
    [InlineData("## Scope\na\n", "## Check")]
    [InlineData("## Scope\na\n## Check\ntext\n## Later\n```sh\ntrue\n```\n", "## Check")]
    public async Task ABodyThatLacksAPartGetsOneCommentAndNoContainer(string body, string heading)
    {
        using FactoryRunHarness h = Make("cleanup");
        h.Body = body;
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(heading, Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
        Assert.Null(result.PullRequest);
    }

    [Fact]
    public async Task APassingCheckBeforeTheBuildStopsTheRunAndRemovesTheCopy()
    {
        using FactoryRunHarness h = Make("cleanup");
        h.CheckResults[0] = (0, "", "");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("The check passes before the build", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "build");
        Assert.Contains(h.Commands, c => c.Arguments.Contains("remove"));
        Assert.Equal(0, result.Redos);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task ACheckThatDoesNotRunGetsItsCodeAndStandardError(int code)
    {
        using FactoryRunHarness h = Make("cleanup");
        h.CheckResults[0] = (code, "", "no such command");
        await h.RunAsync(TestContext.Current.CancellationToken);
        string comment = Assert.Single(h.CommentBodies);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), comment, StringComparison.Ordinal);
        Assert.Contains("no such command", comment, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Prompts, p => p.Kind is "build");
    }

    [Theory]
    [InlineData("src/A.cs", "outside the scope: src/A.cs")]
    [InlineData("docs/README.md", "outside the scope: docs/README.md")]
    [InlineData("specs/features/a.feature", "pin: specs/features/a.feature")]
    public async Task AChangeThatTheScopeOrThePinRefusesGetsARedoBeforeTheReview(string path, string redo)
    {
        using FactoryRunHarness h = Make("cleanup");
        h.FirstBuildChanges.Add(path);
        await h.RunAsync(TestContext.Current.CancellationToken);
        SeenPrompt second = h.Prompts.Where(p => p.Kind is "build").ElementAt(1);
        Assert.Contains(redo, second.Text, StringComparison.Ordinal);
        Assert.True(h.Prompts.FindIndex(p => p.Kind is "review") > h.Prompts.IndexOf(second));
    }

    [Fact]
    public async Task AChangeToASettingsFileTheScopeNamesPassesTheTestGate()
    {
        using FactoryRunHarness h = Make("cleanup");
        h.Body = "## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n";
        h.FirstBuildChanges.Add(".editorconfig");
        h.FirstBuildWrites.Add(".editorconfig");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(h.Prompts, p => p.Text.Contains("pin: .editorconfig", StringComparison.Ordinal));
        Assert.Contains(h.Prompts, p => p.Kind is "review");
        Assert.NotNull(result.PullRequest);
        Assert.Equal(0, result.Redos);
    }

    [Fact]
    public async Task ALostTestGetsARedo()
    {
        using FactoryRunHarness h = Make("cleanup");
        h.BaseOnlyTests.Add(("Specs.Unit.GreeterTests", "Greets"));
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("lost test: Specs.Unit.GreeterTests.Greets", h.Prompts.Where(p => p.Kind is "build").ElementAt(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedCheckAfterABuildGetsARedoWithItsOutput()
    {
        using FactoryRunHarness h = Make("cleanup");
        h.CheckResults[1] = (2, "the out", "the err");
        await h.RunAsync(TestContext.Current.CancellationToken);
        string text = h.Prompts.Where(p => p.Kind is "build").ElementAt(1).Text;
        Assert.Contains("code 2", text, StringComparison.Ordinal);
        Assert.Contains("the out", text, StringComparison.Ordinal);
        Assert.Contains("the err", text, StringComparison.Ordinal);
        Assert.Equal(0, h.Prompts.Take(h.Prompts.IndexOf(h.Prompts.Where(p => p.Kind is "build").ElementAt(1))).Count(p => p.Kind is "review"));
    }

    [Fact]
    public async Task FourRedChecksGiveOneCommentAndNoPush()
    {
        using FactoryRunHarness h = Make("cleanup");
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.CheckResults[build] = (1, "", "still red");
        }

        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("still red", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "git" && c.Arguments[0] is "push");
        Assert.Equal(new FactoryRunResult(null, 3), result);
    }

    [Fact]
    public async Task TheCleanupPromptsNameNoPlan()
    {
        using FactoryRunHarness h = Make("cleanup");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.All(h.Prompts, p => Assert.DoesNotContain(".factory/plan.md", p.Text, StringComparison.Ordinal));
    }
}
