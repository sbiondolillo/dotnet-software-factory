using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ErrorAnswerStopSteps(FactoryRunSteps factory)
{
    private Specs.Unit.FactoryRunHarness H => factory.Harness;

    [Given("claude answers the plan prompt with the error flag set")]
    public void GivenPlanError() => H.PlanIsError = true;

    [Given("claude answers the first build prompt with the error flag set")]
    public void GivenFirstBuildError() => H.ErrorBuilds.Add(1);

    [Given("claude answers the first build prompt with the error flag set and the result text {string}")]
    public void GivenFirstBuildErrorText(string text)
    {
        H.ErrorBuilds.Add(1);
        H.ErrorText = text;
    }

    [Given("claude answers the first build prompt with the error flag set and no result text")]
    public void GivenFirstBuildErrorNoText()
    {
        H.ErrorBuilds.Add(1);
        H.ErrorText = "";
    }

    [Given("claude answers the first review prompt with the error flag set")]
    public void GivenFirstReviewError() => H.ErrorReviews.Add(1);

    [Then("FactoryRun runs these prompts in this order")]
    public void ThenPrompts(Table table)
    {
        List<string> expected = [table.Header.First(), .. table.Rows.Select(row => row[0])];
        Assert.Equal(expected.Skip(1), H.Prompts.Select(p => $"{p.Kind} prompt"));
    }

    [Then("standard output holds the body of the comment that FactoryRun posts on the issue")]
    public void ThenOutputHoldsBody()
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.EndsWith(body, H.Progress.ToString().TrimEnd('\n'), StringComparison.Ordinal);
    }
}
