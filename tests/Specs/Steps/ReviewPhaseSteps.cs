using System.Globalization;
using System.Text;
using Core;
using Moq;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ReviewPhaseSteps(IssueState issue, ExceptionState exceptionState, ReviewPromptState promptState) : IDisposable
{
    private readonly ReviewPhaseHarness _h = new();
    private string _model = "";
    private int _turns;
    private string _base = "";
    private ReviewPhaseResult? _result;
    private Mock<CommandRunner>? _gitMock;
    private string _gitDiff = "";
    private int _gitExit;
    private string _gitText = "";

    public void Dispose() => _h.Dispose();

    private ReviewPhaseResult Result => _result ?? throw new InvalidOperationException("the review phase did not run");

    private string Prompt => promptState.Text ?? _h.Prompts[0];

    [Given("the review model is {string} and the turn limit is {int}")]
    public void GivenModel(string model, int turns)
    {
        _model = model;
        _turns = turns;
    }

    [Given("the base commit is {string}")]
    public void GivenBase(string commit) => _base = commit;

    [Given("claude answers with the error flag clear")]
    public void GivenFlagClear() => _h.IsError = false;

    [Given("claude answers with the error flag set and the result text {string}")]
    public void GivenFlagSet(string text)
    {
        _h.IsError = true;
        _h.ResultText = text;
    }

    [Given("claude answers with the error flag set and no result text")]
    public void GivenFlagSetNoText()
    {
        _h.IsError = true;
        _h.ResultText = "";
    }

    [Given("git answers the diff with the text {string}")]
    public void GivenDiffText(string text)
    {
        _h.DiffText = text;
        _gitDiff = text;
    }

    [Given("git answers the diff with the letter {string} repeated {int} times")]
    public void GivenDiffLetters(string letter, int count)
    {
        _h.DiffText = string.Concat(Enumerable.Repeat(letter, count));
        _gitDiff = _h.DiffText;
    }

    [Given("git exits with the code {int} for the diff")]
    public void GivenDiffExit(int code)
    {
        _h.DiffExitCode = code;
        _gitExit = code;
    }

    [Given("the agent writes {string} as the verdict")]
    public void GivenAgentWrites(string verdict) => _h.VerdictToWrite = verdict;

    [Given("the agent writes no verdict file")]
    public void GivenAgentWritesNothing() => _h.VerdictToWrite = null;

    [Given("the work tree holds {string} in the verdict file before the review")]
    public void GivenTreeHolds(string verdict) => _h.Write(".factory/review.json", verdict);

    [When("I ask Git for the diff of the work tree against the base commit")]
    public async Task WhenAskGit()
    {
        _gitMock = new Mock<CommandRunner>(MockBehavior.Strict);
        _gitMock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = _gitExit, StandardOutput = _gitDiff, StandardError = "fatal: bad object" });
        try
        {
            _gitText = await new Git(_gitMock.Object).ReadDiffAsync(_base, _h.WorkTree);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("I run the review phase")]
    public async Task WhenRun() => _result = await _h.RunAsync(issue.Title, issue.Body, _model, _turns, _base);

    [Then("Git gives back the text {string}")]
    public void ThenGitText(string text) => Assert.Equal(text, _gitText);

    [Then("Git throws a GitCommandException")]
    public void ThenGitThrows() => Assert.IsType<GitCommandException>(exceptionState.Exception);

    [Then("the review phase ran one prompt")]
    public void ThenOnePrompt() => Assert.Single(_h.Prompts);

    [Then("the review prompt ran with the model {string} and the turn limit {int}")]
    public void ThenModel(string model, int turns)
    {
        List<string> args = _h.ExecArguments[0];
        Assert.Equal(model, args[args.IndexOf("--model") + 1]);
        Assert.Equal(turns.ToString(CultureInfo.InvariantCulture), args[args.IndexOf("--max-turns") + 1]);
    }

    [Then("the review prompt holds {string} and {string}")]
    public void ThenHolds2(string a, string b) => AssertHolds(a, b);

    [Then("the review prompt holds {string}, {string} and {string}")]
    public void ThenHolds3(string a, string b, string c) => AssertHolds(a, b, c);

    [Then("the review prompt holds {string}")]
    public void ThenHolds1(string a) => AssertHolds(a);

    [Then("the review prompt is {int} bytes or fewer when encoded")]
    public void ThenBytes(int limit) => Assert.True(Encoding.UTF8.GetByteCount(Prompt) <= limit);

    [Then("the review prompt holds the letter {string} {int} times in sequence")]
    public void ThenLetters(string letter, int count) =>
        Assert.Contains(string.Concat(Enumerable.Repeat(letter, count)), Prompt);

    [Then("the review gives back an approval")]
    public void ThenApproval()
    {
        Assert.True(Result.Approved);
        Assert.Empty(Result.Blocking);
    }

    [Then("the review gives back a rejection whose one blocking item is {string}")]
    public void ThenRejectionItem(string item)
    {
        Assert.False(Result.Approved);
        Assert.Equal(item, Assert.Single(Result.Blocking));
    }

    [Then("the review gives back a rejection with one blocking item that holds {string}")]
    public void ThenRejectionHolds(string text)
    {
        Assert.False(Result.Approved);
        Assert.Contains(text, Assert.Single(Result.Blocking), StringComparison.Ordinal);
    }

    [Then("the review gives back a rejection with one blocking item")]
    public void ThenRejectionOne()
    {
        Assert.False(Result.Approved);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(Result.Blocking)));
    }

    private void AssertHolds(params string[] texts)
    {
        foreach (string text in texts)
        {
            Assert.Contains(text, Prompt, StringComparison.Ordinal);
        }
    }
}
