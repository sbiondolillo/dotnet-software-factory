using System.Globalization;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class PlanPhaseSteps(IssueState issue) : IDisposable
{
    private readonly PlanPhaseHarness _h = new();
    private string _model = "";
    private int _turns;
    private PlanPhaseResult? _result;

    public void Dispose() => _h.Dispose();

    private PlanPhaseResult Result => _result ?? throw new InvalidOperationException("the plan phase did not run");

    [Given("an empty work tree")]
    public static void GivenAnEmptyWorkTree()
    {
    }

    [Given("the issue has the title {string} and the body {string}")]
    public void GivenTheIssue(string title, string body)
    {
        issue.Title = title;
        issue.Body = body;
    }

    [Given("the model is {string} and the turn limit is {int}")]
    public void GivenTheModel(string model, int turns)
    {
        _model = model;
        _turns = turns;
    }

    [Given("the agent on the first run writes the sound feature file and the plan")]
    public void GivenFirstWritesSound() => _h.AgentWritesSoundFiles(1);

    [Given("the work tree holds the sound feature file before the first prompt")]
    public void GivenTreeHoldsSound() => _h.Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);

    [Given("the agent on the first run adds the second scenario to the sound feature file and writes the plan")]
    public void GivenFirstAddsSecond()
    {
        _h.AgentWrites(1, "specs/features/checkout.feature", PlanPhaseHarness.TwoScenarioFeature);
        _h.AgentWrites(1, ".factory/plan.md", PlanPhaseHarness.PlanText);
    }

    [Given("the agent on the first run writes the file {string}")]
    public void GivenFirstWritesFile(string path) => _h.AgentWrites(1, path, "x");

    [Given("the agent on every run writes the plan")]
    public void GivenEveryWritesPlan() => _h.AgentWrites(null, ".factory/plan.md", PlanPhaseHarness.PlanText);

    [Given("the agent on every run writes the sound feature file")]
    public void GivenEveryWritesSound() => _h.AgentWrites(null, "specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);

    [Given("the agent on every run writes the sound feature file and the plan")]
    public void GivenEveryWritesSoundAndPlan() => _h.AgentWritesSoundFiles(null);

    [Given("the agent on every run writes the file {string} with the text {string}")]
    public void GivenEveryWritesText(string path, string text) => _h.AgentWrites(null, path, text);

    [Given("the agent on every run writes the file {string} with no text")]
    public void GivenEveryWritesEmpty(string path) => _h.AgentWrites(null, path, "");

    [Given("the work tree holds the file {string} with the text {string} before the first prompt")]
    public void GivenTreeHoldsFile(string path, string text) => _h.Write(path, text);

    [Given("the agent on every run changes one byte of {string}")]
    public void GivenEveryChanges(string path) => _h.Act(null, root =>
    {
        string full = Path.Combine(root, path);
        byte[] bytes = File.ReadAllBytes(full);
        bytes[0] = bytes[0] is (byte)'Z' ? (byte)'Y' : (byte)'Z';
        File.WriteAllBytes(full, bytes);
    });

    [Given("the agent on every run removes {string}")]
    public void GivenEveryRemoves(string path) => _h.AgentRemoves(null, path);

    [Given("the agent on the first run writes the sound feature file, the plan and the file {string}")]
    public void GivenFirstWritesSoundAndFile(string path)
    {
        _h.AgentWritesSoundFiles(1);
        _h.AgentWrites(1, path, "class Extra {}");
    }

    [Given("the agent on the second run removes {string}")]
    public void GivenSecondRemoves(string path) => _h.AgentRemoves(2, path);

    [Given("the agent on the second run writes the file {string} with the text {string}")]
    public void GivenSecondWrites(string path, string text) => _h.AgentWrites(2, path, text);

    [Given("claude answers each prompt with the error flag set and the result text {string}")]
    public void GivenEachError(string text) => _h.Answer(null, true, text);

    [Given("claude answers the first run with the error flag set and no result text")]
    public void GivenFirstErrorNoText() => _h.Answer(1, true, "");

    [When("I run the plan phase")]
    public async Task WhenIRun() =>
        _result = await _h.RunAsync(issue.Title, issue.Body, _model, _turns);

    [Then("the first prompt ran with the model {string} and the turn limit {int}")]
    public void ThenFirstModel(string model, int turns)
    {
        List<string> args = _h.ExecArguments[0];
        Assert.Equal(model, args[args.IndexOf("--model") + 1]);
        Assert.Equal(turns.ToString(CultureInfo.InvariantCulture), args[args.IndexOf("--max-turns") + 1]);
    }

    [Then("the first prompt holds {string}, {string}, {string} and {string}")]
    public void ThenFirstHolds(string a, string b, string c, string d) => AssertHolds(_h.Prompts[0], a, b, c, d);

    [Then("the second prompt holds {string}, {string} and {string}")]
    public void ThenSecondHolds(string a, string b, string c) => AssertHolds(_h.Prompts[1], a, b, c);

    [Then("the plan phase ran one prompt")]
    public void ThenOnePrompt() => Assert.Single(_h.Prompts);

    [Then("the plan phase ran two prompts")]
    public void ThenTwoPrompts() => Assert.Equal(2, _h.Prompts.Count);

    [Then("the result is passed")]
    public void ThenPassed() => Assert.True(Result.Passed, string.Join("; ", Result.Failures));

    [Then("the result is failed")]
    public void ThenFailed() => Assert.False(Result.Passed);

    [Then("the pin holds {string} with the hash of its bytes")]
    public void ThenPinHash(string path)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(_h.WorkTree, path));
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            Result.Pin!.Hashes[path]);
    }

    [Then("the pin holds {string}")]
    public void ThenPinHolds(string path) => Assert.Contains(path, Result.Pin!.Hashes.Keys);

    [Then("a failure names {string}")]
    [Then("the failure names {string}")]
    [Then("a failure holds {string}")]
    public void ThenFailureNames(string text) => Assert.Contains(Result.Failures, f => f.Contains(text, StringComparison.Ordinal));

    [Then("the result holds {int} failure")]
    public void ThenHoldsFailures(int count) => Assert.Equal(count, Result.Failures.Count);

    [Then("each command the plan phase ran is a docker command")]
    public void ThenAllDocker()
    {
        Assert.NotEmpty(_h.Recorded);
        Assert.All(_h.Recorded, spec => Assert.Equal("docker", spec.Command));
    }

    private static void AssertHolds(string prompt, params string[] texts)
    {
        foreach (string text in texts)
        {
            Assert.Contains(text, prompt);
        }
    }
}
