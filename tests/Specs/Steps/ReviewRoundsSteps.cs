using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ReviewRoundsSteps(ConsoleProgramSteps console)
{
    private ConsoleHarness H => console.Harness;

    /// <summary>The feedback runs so far: every clone after the first, which is the factory run's.</summary>
    private int FeedbackRuns => Math.Max(0, H.Commands.Count(c => c.Command is "git" && c.Arguments[0] is "clone") - 1);

    [Given("each feedback run changes the head commit of the pull request")]
    public void GivenHeadChanges() => H.HeadSha = _ => $"{FeedbackRuns:x8}{ConsoleHarness.DefaultSha[8..]}";

    [Given("each feedback run leaves the head commit of the pull request as it was")]
    public void GivenHeadStays() => H.HeadSha = _ => ConsoleHarness.DefaultSha;

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
