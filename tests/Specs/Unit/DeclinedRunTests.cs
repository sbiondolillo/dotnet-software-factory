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
        RunResult<Delivered> result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RunEnd.Declined, result.Stop?.End);
        Assert.Equal(RecipeRules.Parse(["cleanup", "refactor"], Body).Declined, Assert.Single(h.CommentBodies));
        Assert.Empty(h.AgentRequests);
        Assert.Null(result.Passed);
        Assert.Equal(0, result.Redos);
    }

    [Fact]
    public async Task AScenarioFaultDeclinesTheIssue()
    {
        using RunHarness h = Make("## Scenarios\n\nThe scenarios follow.\n");
        RunResult<Delivered> result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RunEnd.Declined, result.Stop?.End);
        Assert.Single(h.CommentBodies);
        Assert.Null(result.Passed);
        Assert.Equal(0, result.Redos);
    }

    [Fact]
    public async Task AnAbsentPathOfTheLayoutDoesNotDeclineTheIssue()
    {
        using RunHarness h = Make(Body, "cleanup");
        h.Lacks.Add("Solution.slnx");
        RunResult<Delivered> result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RunEnd.Stopped, result.Stop?.End);
        Assert.Single(h.CommentBodies);
        Assert.Null(result.Passed);
        Assert.Equal(0, result.Redos);
    }

    [Fact]
    public async Task ACheckThatPassesBeforeTheBuildDoesNotDeclineTheIssue()
    {
        using RunHarness h = Make(Body, "cleanup");
        h.CheckResults[0] = (0, "", "");
        RunResult<Delivered> result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RunEnd.Stopped, result.Stop?.End);
    }

    [Fact]
    public async Task AnIssueThatRunsDoesNotDecline()
    {
        using RunHarness h = Make("The checkout shows the total of the cart.", "factory");
        RunResult<Delivered> result = await h.RunFactoryAsync(TestContext.Current.CancellationToken);
        Assert.Null(result.Stop);
    }
}
