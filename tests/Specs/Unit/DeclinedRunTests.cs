using Core;
using Xunit;

namespace Specs.Unit;

public sealed class DeclinedRunTests
{
    private const string Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ntrue\n```\n";

    private static FactoryRunHarness Make(string body, params string[] labels)
    {
        var h = new FactoryRunHarness { Title = "Fix it", Body = body };
        h.Labels.AddRange(labels);
        return h;
    }

    [Theory]
    [InlineData("cleanup", "reframe")]
    [InlineData("behavior", "reframe")]
    [InlineData("refactor", "reframe")]
    [InlineData("behavior", "refactor")]
    [InlineData("cleanup", "refactor")]
    [InlineData("behavior", "cleanup")]
    public async Task TwoLabelsThatExcludeEachOtherDeclineTheIssue(string first, string second)
    {
        using FactoryRunHarness h = Make(Body, first, second);
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(h.Declined);
        Assert.Single(h.CommentBodies);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Theory]
    [InlineData("cleanup", "Fix the install test.")]
    [InlineData("reframe", "Fix the install test.")]
    [InlineData("cleanup", "## Scope\nREADME.md\n")]
    [InlineData("reframe", "## Scope\nREADME.md\n")]
    [InlineData("", "## Scenarios\n\nThe scenarios follow.\n")]
    public async Task ABodyWithAFaultDeclinesTheIssue(string label, string body)
    {
        using FactoryRunHarness h = Make(body, label.Length is 0 ? [] : [label]);
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(h.Declined);
        Assert.Single(h.CommentBodies);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task AnAbsentPathOfTheLayoutDoesNotDeclineTheIssue()
    {
        using FactoryRunHarness h = Make(Body, "cleanup");
        h.Lacks.Add("Solution.slnx");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.False(h.Declined);
        Assert.Single(h.CommentBodies);
        Assert.Equal(new FactoryRunResult(null, 0), result);
    }

    [Fact]
    public async Task ACheckThatPassesBeforeTheBuildDoesNotDeclineTheIssue()
    {
        using FactoryRunHarness h = Make(Body, "cleanup");
        h.CheckResults[0] = (0, "", "");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.False(h.Declined);
    }

    [Fact]
    public async Task AnIssueThatRunsDoesNotDecline()
    {
        using FactoryRunHarness h = Make("The checkout shows the total of the cart.", "factory");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.False(h.Declined);
    }
}
