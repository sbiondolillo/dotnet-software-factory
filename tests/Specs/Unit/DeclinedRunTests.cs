using Core;
using Xunit;

namespace Specs.Unit;

public sealed class DeclinedRunTests
{
    private const string Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ntrue\n```\n";

    private static RunHarness Make(string body, params string[] labels)
    {
        var h = new RunHarness { Title = "Fix it", Body = body };
        h.Labels.AddRange(labels);
        return h;
    }

    [Fact]
    public async Task ADeclineOfTheRecipeRulesPostsItsTextOnceAndRunsNothing()
    {
        using RunHarness h = Make(Body, "cleanup", "refactor");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.True(h.Declined);
        Assert.Equal(RecipeRules.Parse(["cleanup", "refactor"], Body).Declined, Assert.Single(h.CommentBodies));
        Assert.Empty(h.AgentRequests);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task AScenarioFaultDeclinesTheIssue()
    {
        using RunHarness h = Make("## Scenarios\n\nThe scenarios follow.\n");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.True(h.Declined);
        Assert.Single(h.CommentBodies);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task AnAbsentPathOfTheLayoutDoesNotDeclineTheIssue()
    {
        using RunHarness h = Make(Body, "cleanup");
        h.Lacks.Add("Solution.slnx");
        FactoryRunResult result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.False(h.Declined);
        Assert.Single(h.CommentBodies);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task ACheckThatPassesBeforeTheBuildDoesNotDeclineTheIssue()
    {
        using RunHarness h = Make(Body, "cleanup");
        h.CheckResults[0] = (0, "", "");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.False(h.Declined);
    }

    [Fact]
    public async Task AnIssueThatRunsDoesNotDecline()
    {
        using RunHarness h = Make("The checkout shows the total of the cart.", "factory");
        await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.False(h.Declined);
    }
}
