using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CleanupRunTests
{
    private static RunHarness Make(params string[] labels)
    {
        var h = new RunHarness { Title = "Fix it", Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ntrue\n```\n" };
        h.Labels.AddRange(labels);
        return h;
    }

    [Fact]
    public async Task AnIssueWithOtherLabelsRunsThePlanPhase()
    {
        using RunHarness h = Make("factory");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(h.Builder.Of(Phase.Plan));
    }

    [Fact]
    public async Task BothLabelsGetOneCommentAndNoContainer()
    {
        using RunHarness h = Make("cleanup", "behavior");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
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
        using RunHarness h = Make("cleanup");
        h.Body = body;
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains(heading, Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
        Assert.Null(result.PullRequest);
    }

    [Fact]
    public async Task APassingCheckBeforeTheBuildStopsTheRunAndRemovesTheCopy()
    {
        using RunHarness h = Make("cleanup");
        h.CheckResults[0] = (0, "", "");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("The check passes before the build", Assert.Single(h.CommentBodies), StringComparison.Ordinal);
        Assert.Empty(h.Builder.Of(Phase.Build));
        Assert.Contains(h.Commands, c => c.Arguments.Contains("remove"));
        Assert.Equal(0, result.Redos);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task ACheckThatDoesNotRunGetsItsCodeAndStandardError(int code)
    {
        using RunHarness h = Make("cleanup");
        h.CheckResults[0] = (code, "", "no such command");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        string comment = Assert.Single(h.CommentBodies);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), comment, StringComparison.Ordinal);
        Assert.Contains("no such command", comment, StringComparison.Ordinal);
        Assert.Empty(h.Builder.Of(Phase.Build));
    }

    [Theory]
    [InlineData("src/A.cs", "outside the scope: src/A.cs")]
    [InlineData("docs/README.md", "outside the scope: docs/README.md")]
    [InlineData("specs/features/a.feature", "pin: specs/features/a.feature")]
    public async Task AChangeThatTheScopeOrThePinRefusesGetsARedoBeforeTheReview(string path, string redo)
    {
        using RunHarness h = Make("cleanup");
        h.FirstBuildChanges.Add(path);
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        AgentRequest second = h.Builder.Of(Phase.Build).ElementAt(1);
        Assert.Contains(redo, second.Prompt.Text, StringComparison.Ordinal);
        Assert.True(h.AgentRequests.FindIndex(r => r.Phase is Phase.Review) > h.AgentRequests.IndexOf(second));
        Assert.Contains($"- Redo 1, after scope 1: {redo}", h.Summary.Split('\n'));
    }

    [Fact]
    public async Task AChangeToASettingsFileTheScopeNamesPassesTheTestGate()
    {
        using RunHarness h = Make("cleanup");
        h.Body = "## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n";
        h.FirstBuildChanges.Add(".editorconfig");
        h.BuildWrites(1, ".editorconfig");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(h.AgentRequests, r => r.Prompt.Text.Contains("pin: .editorconfig", StringComparison.Ordinal));
        Assert.NotEmpty(h.Reviewer.Of(Phase.Review));
        Assert.NotNull(result.PullRequest);
        Assert.Equal(0, result.Redos);
    }

    [Fact]
    public async Task ALostTestGetsARedo()
    {
        using RunHarness h = Make("cleanup");
        h.BaseOnlyTests.Add(("Specs.Unit.GreeterTests", "Greets"));
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Contains("lost test: Specs.Unit.GreeterTests.Greets", h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedCheckAfterABuildGetsARedoWithItsOutput()
    {
        using RunHarness h = Make("cleanup");
        h.CheckResults[1] = (2, "the out", "the err");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        string text = h.Builder.Of(Phase.Build).ElementAt(1).Prompt.Text;
        Assert.Contains("code 2", text, StringComparison.Ordinal);
        Assert.Contains("the out", text, StringComparison.Ordinal);
        Assert.Contains("the err", text, StringComparison.Ordinal);
        Assert.Equal(0, h.AgentRequests.Take(h.AgentRequests.IndexOf(h.Builder.Of(Phase.Build).ElementAt(1))).Count(r => r.Phase is Phase.Review));
    }

    [Fact]
    public async Task TheCleanupPromptsNameNoPlan()
    {
        using RunHarness h = Make("cleanup");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.All(h.AgentRequests, r => Assert.DoesNotContain(".factory/plan.md", r.Prompt.Text, StringComparison.Ordinal));
    }
}
