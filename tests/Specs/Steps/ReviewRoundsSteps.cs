using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ReviewRoundsSteps(ConsoleProgramSteps console)
{
    private SettleHarness H => console.Harness;

    /// <summary>The feedback runs so far: each line <c>feedback: start</c>, which a feedback run writes before its clone.</summary>
    private int FeedbackRuns => SettleHarness.Lines(H.Out).Count(line => line is "feedback: start");

    [Given("each feedback run changes the head commit of the pull request")]
    public void GivenHeadChanges() => H.HeadSha = _ => $"{FeedbackRuns:x8}{SettleHarness.DefaultSha[8..]}";

    [Given("each feedback run leaves the head commit of the pull request as it was")]
    public void GivenHeadStays() => H.Threads = "watch-only";

    [Then("standard output holds {string}, {string}, {string}, {string}, {string}, {string} and {string}")]
    public void ThenOutputSeven(string a, string b, string c, string d, string e, string f, string g)
    {
        string output = console.Harness.Out.ToString();
        foreach (string value in new[] { a, b, c, d, e, f, g })
        {
            Assert.Contains(value, output, StringComparison.Ordinal);
        }
    }

    [Then("the run starts a second feedback run")]
    public void ThenSecond() => Assert.True(FeedbackRuns >= 2);

    [Then("the run starts {int} feedback runs")]
    public void ThenStarts(int count) => Assert.Equal(count, FeedbackRuns);

    [Then("the run starts no feedback run")]
    public void ThenNone() => Assert.Equal(0, FeedbackRuns);
}
