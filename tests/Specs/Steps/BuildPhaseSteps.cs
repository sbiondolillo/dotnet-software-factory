using System.Globalization;
using System.Text;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class BuildPhaseSteps(IssueState issue) : IDisposable
{
    private readonly BuildPhaseHarness _h = new();
    private string _model = "";
    private int _turns;
    private BuildPhaseResult? _result;

    /// <summary>Gives the first build prompt of another run, so a feature of that run shares the steps of this class.</summary>
    public Func<string>? FirstPrompt { get; set; }

    public void Dispose() => _h.Dispose();

    private BuildPhaseResult Result => _result ?? throw new InvalidOperationException("the build phase did not run");

    [Given("the build model is {string} and the turn limit is {int}")]
    public void GivenModel(string model, int turns)
    {
        _model = model;
        _turns = turns;
    }

    [Given("the build agent answers with the error flag clear")]
    public void GivenClear() => _h.IsError = false;

    [Given("the build agent answers with the error flag set and the result text {string}")]
    public void GivenSetWithText(string text)
    {
        _h.IsError = true;
        _h.ResultText = text;
    }

    [Given("the build agent answers with the error flag set and no result text")]
    public void GivenSetNoText()
    {
        _h.IsError = true;
        _h.OmitResult = true;
    }

    [When("I run the build phase with no Redo texts")]
    public async Task WhenNoRedo() => await Run([]);

    [When("I run the build phase with the Redo texts {string} and {string}")]
    public async Task WhenTwoRedo(string first, string second) => await Run([first, second]);

    [When("I run the build phase with the Redo texts of the letter {string} {int} times and {string}")]
    public async Task WhenLetterAndText(string letter, int count, string text) => await Run([Repeat(letter, count), text]);

    [When("I run the build phase with one Redo text of the letter {string} {int} times")]
    public async Task WhenOneLetter(string letter, int count) => await Run([Repeat(letter, count)]);

    [Then("the build phase ran one prompt")]
    public void ThenOnePrompt() => Assert.Single(_h.Prompts);

    [Then("the build prompt ran with the model {string} and the turn limit {int}")]
    public void ThenModel(string model, int turns)
    {
        List<string> args = _h.ExecArguments[0];
        Assert.Equal(model, args[args.IndexOf("--model") + 1]);
        Assert.Equal(turns.ToString(CultureInfo.InvariantCulture), args[args.IndexOf("--max-turns") + 1]);
    }

    [Then("the build prompt holds {string} and {string}")]
    public void ThenHolds2(string a, string b) => AssertHolds(a, b);

    [Then("the build prompt holds {string}, {string}, {string} and {string}")]
    public void ThenHolds4(string a, string b, string c, string d) => AssertHolds(a, b, c, d);

    [Then("the build prompt holds {string}, {string} and {string}")]
    public void ThenHolds3(string a, string b, string c) => AssertHolds(a, b, c);

    [Then("the build prompt holds {string}, {string}, {string}, {string}, {string}, {string} and {string}")]
    public void ThenHolds7(string a, string b, string c, string d, string e, string f, string g) => AssertHolds(a, b, c, d, e, f, g);

    [Then("the build prompt is {int} bytes or fewer")]
    public void ThenBytes(int limit) => Assert.True(Encoding.UTF8.GetByteCount(_h.Prompts[0]) <= limit);

    [Then("the build prompt holds the letter {string} {int} times in sequence")]
    public void ThenSequence(string letter, int count) => Assert.Contains(Repeat(letter, count), _h.Prompts[0], StringComparison.Ordinal);

    [Then("the build prompt holds the letter {string} {int} times in sequence and {string}")]
    public void ThenSequenceAnd(string letter, int count, string text)
    {
        Assert.Contains(Repeat(letter, count), _h.Prompts[0], StringComparison.Ordinal);
        AssertHolds(text);
    }

    [Then("the build result is passed")]
    public void ThenPassed() => Assert.True(Result.Passed);

    [Then("the build result is failed")]
    public void ThenFailed() => Assert.False(Result.Passed);

    [Then("the build failure holds {string}")]
    public void ThenFailure(string text) => Assert.Contains(text, Result.Failure, StringComparison.Ordinal);

    private static string Repeat(string letter, int count) => string.Concat(Enumerable.Repeat(letter, count));

    private async Task Run(List<string> redo) =>
        _result = await _h.RunAsync(issue.Title, issue.Body, _model, _turns, redo);

    private void AssertHolds(params string[] texts)
    {
        foreach (string text in texts)
        {
            Assert.Contains(text, FirstPrompt is null ? _h.Prompts[0] : FirstPrompt(), StringComparison.Ordinal);
        }
    }
}
