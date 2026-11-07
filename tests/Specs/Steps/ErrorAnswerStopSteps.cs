using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ErrorAnswerStopSteps(FactoryRunSteps factory)
{
    private RunHarness H => factory.Harness;

    [Given("claude answers the plan prompt with the error flag set")]
    public void GivenPlanError() => H.Builder.Answer(Phase.Plan, FakeAgent.Error("plan went wrong"));

    /// <summary>An adapter gives <see cref="Agent.NoTextError"/> for an error answer with no text, as ClaudeCodeAgentTests states.</summary>
    [Given("claude answers the first build prompt with the error flag set")]
    public void GivenFirstBuildError() => H.Builder.Answer(Phase.Build, FakeAgent.Error(Agent.NoTextError), 1);

    [Given("claude answers the first build prompt with the error flag set and the result text {string}")]
    public void GivenFirstBuildErrorText(string text) => H.Builder.Answer(Phase.Build, FakeAgent.Error(text), 1);

    [Given("claude answers the first build prompt with the error flag set and no result text")]
    public void GivenFirstBuildErrorNoText() => H.Builder.Answer(Phase.Build, FakeAgent.Error(Agent.NoTextError), 1);

    [Given("claude answers the first review prompt with the error flag set")]
    public void GivenFirstReviewError()
    {
        H.Reviewer.Answer(Phase.Review, FakeAgent.Error(Agent.NoTextError), 1);
        H.Reviewer.Stage(Phase.Review, "review.json", null, 1);
    }

    [Then("FactoryRun runs these prompts in this order")]
    public void ThenPrompts(Table table)
    {
        List<string> expected = [table.Header.First(), .. table.Rows.Select(row => row[0])];
        Assert.Equal(expected.Skip(1), H.AgentRequests.Select(r => $"{r.Phase.Text()} prompt"));
    }

    [Then("standard output holds the body of the comment that FactoryRun posts on the issue")]
    public void ThenOutputHoldsBody()
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.EndsWith(body, H.Progress.ToString().TrimEnd('\n'), StringComparison.Ordinal);
    }
}
