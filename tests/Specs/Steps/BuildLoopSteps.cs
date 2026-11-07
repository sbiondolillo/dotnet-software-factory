using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class BuildLoopSteps : IAsyncDisposable
{
    private readonly BuildLoopHarness _harness = new();

    public async ValueTask DisposeAsync() => await _harness.DisposeAsync();

    [Given("^a build loop with the gates (.+)$")]
    public void GivenGates(string list) => _harness.Gates.AddRange(list.Replace(" and ", ", ", StringComparison.Ordinal).Split(", ").Select(Name));

    [Given("the gate {word} fails once with the Redo text {string}")]
    public void GivenGateFailsOnce(string gate, string text) =>
        _harness.GateAnswers[Name(gate)] = [BuildLoopHarness.GateFails(text), () => new GateOutcome("passed", [])];

    [Given("the review blocks once with the Redo text {string}")]
    public void GivenReviewBlocksOnce(string text)
    {
        _harness.Reviews.Clear();
        _harness.Reviews.AddRange([BuildLoopHarness.ReviewBlocks(text), BuildLoopHarness.Approve]);
    }

    [Given("the review blocks every time with the Redo text {string}")]
    public void GivenReviewBlocksEveryTime(string text) => _harness.Reviews[0] = BuildLoopHarness.ReviewBlocks(text);

    [Given("the build answers with the error {string}")]
    public void GivenBuildError(string text) => _harness.Builds[0] = BuildLoopHarness.BuildStop(text);

    [Given("the review answers with the error {string}")]
    public void GivenReviewError(string text) => _harness.Reviews[0] = BuildLoopHarness.ReviewError(text);

    [When("the build loop runs")]
    public async Task WhenTheLoopRuns()
    {
        await _harness.RunAsync();
        Assert.Null(_harness.Thrown);
    }

    [Then("^the loop called (.+) in that order$")]
    public void ThenCalls(string calls) => Assert.Equal(calls.Split(", "), _harness.Calls);

    [Then("the loop passed with {int} Redos")]
    public void ThenPassed(int redos) => Assert.Equal(new BuildLoopResult(BuildLoopEnd.Passed, redos), _harness.Result);

    [Then("the loop stopped with {int} Redos and the comment {string}")]
    public void ThenStopped(int redos, string comment) =>
        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, redos, comment), _harness.Result);

    [Then("the loop spent its budget with {int} Redos")]
    public void ThenSpent(int redos)
    {
        Assert.Equal(BuildLoopEnd.Spent, _harness.Result?.End);
        Assert.Equal(redos, _harness.Result?.Redos);
    }

    [Then("the comment of the loop is {string} and the text {string}")]
    public void ThenComment(string lead, string text) => Assert.Equal($"{lead}\n\n{text}", _harness.Result?.Comment);

    [Then("the loop ran {int} builds")]
    public void ThenBuilds(int count) => Assert.Equal(count, _harness.Calls.Count(call => call is "build"));

    [Then("the job summary holds the item {string}")]
    public void ThenItem(string item) => Assert.Contains(item, _harness.RedoItems);

    [Then("the job summary holds the row {string}")]
    public void ThenRow(string row) => Assert.Contains(row, _harness.Rows);

    [Then("^the progress lines are (.+)$")]
    public void ThenLines(string lines) => Assert.Equal(lines.Split(", "), _harness.Lines);

    [Then("the second build got the Redo text {string}")]
    public void ThenSecondBuild(string text) => Assert.Equal([text], _harness.BuildTexts[1]);

    private static GateName Name(string text) => Enum.GetValues<GateName>().Single(name => name.Text() == text);
}
