using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class JobSummarySteps : IDisposable
{
    private readonly JobSummaryHarness _h = new();
    private FactoryRunResult? _result;

    public void Dispose() => _h.Dispose();

    private static int Index(string word) => word switch
    {
        "first" => 1,
        "second" => 2,
        _ => throw new ArgumentException($"unknown ordinal {word}", nameof(word)),
    };

    private string[] Lines => _h.Summary.Split('\n');

    private string Row(string name) =>
        Lines.Single(line => line.StartsWith($"| {name} |", StringComparison.Ordinal));

    private static string[] Cells(string row) => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];

    [Given("the environment variable GITHUB_STEP_SUMMARY names an empty file")]
    public void GivenEmptyFile() => File.WriteAllText(_h.SummaryPath!, "");

    [Given("the environment variable GITHUB_STEP_SUMMARY names a file that holds the text {string}")]
    public void GivenFileText(string text) => File.WriteAllText(_h.SummaryPath!, text);

    [Given("the issue has no recipe label")]
    public void GivenNoLabel() => Assert.Empty(_h.Labels);

    [Given("the issue has the label {string}")]
    public void GivenLabel(string label)
    {
        _h.Labels.Add(label);
        if (label is "cleanup")
        {
            _h.Body = JobSummaryHarness.CleanupBody;
        }
    }

    [Given("the issue has the label {string} and a {string}")]
    public void GivenLabelAndCheck(string label, string heading)
    {
        Assert.Equal("## Check", heading);
        _h.Labels.Add(label);
        _h.Body = JobSummaryHarness.CleanupBody;
    }

    [Given("the issue has the label {string} and no {string}")]
    public void GivenLabelNoCheck(string label, string heading)
    {
        Assert.Equal("## Check", heading);
        _h.Labels.Add(label);
        _h.Body = "Move the checkout total into its own class.";
    }

    [Given("the plan answer has {int} turns and the cost {string}, {int} seconds after its prompt")]
    public void GivenPlan(int turns, string cost, int seconds) => SetPlan(0, turns, cost, seconds);

    [Given("the {word} plan answer has {int} turns and the cost {string}, {int} seconds after its prompt")]
    public void GivenNthPlan(string which, int turns, string cost, int seconds) => SetPlan(Index(which) - 1, turns, cost, seconds);

    private void SetPlan(int index, int turns, string cost, int seconds)
    {
        while (_h.PlanAnswers.Count <= index)
        {
            _h.PlanAnswers.Add(new SummaryAnswer(1, "0", 0));
        }

        _h.PlanAnswers[index] = _h.PlanAnswers[index] with { Turns = turns, Cost = cost, Seconds = seconds };
    }

    [Given("the plan phase sends two prompts")]
    public void GivenTwoPrompts()
    {
        _h.FirstPlanWritesNothing = true;
        while (_h.PlanAnswers.Count < 2)
        {
            _h.PlanAnswers.Add(new SummaryAnswer(1, "0", 0));
        }
    }

    [Given("the plan phase fails after one prompt with {int} turns and the cost {string}, {int} seconds after the prompt")]
    public void GivenPlanFails(int turns, string cost, int seconds) =>
        _h.PlanAnswers.Add(new SummaryAnswer(turns, cost, seconds, IsError: true, Text: "the plan went wrong"));

    [Given("the {word} build answer has {int} turns and the cost {string}, {int} seconds after its prompt")]
    public void GivenBuild(string which, int turns, string cost, int seconds) =>
        _h.BuildAnswers[Index(which)] = new SummaryAnswer(turns, cost, seconds);

    [Given("the {word} build answer comes {int} seconds after its prompt")]
    public void GivenBuildComes(string which, int seconds) =>
        _h.BuildAnswers[Index(which)] = new SummaryAnswer(1, "0", seconds);

    [Given("the lint gate after the {word} build takes {int} seconds")]
    public void GivenLint(string which, int seconds) => _h.LintSeconds[Index(which)] = seconds;

    [Given("the review answer has {int} turns and the cost {string}, {int} seconds after its prompt")]
    public void GivenReview(int turns, string cost, int seconds) =>
        _h.ReviewAnswers[1] = new SummaryAnswer(turns, cost, seconds);

    [Given("the {word} test gate takes {int} seconds and is red with the Redo texts {string} and {string}")]
    public void GivenRed(string which, int seconds, string first, string second)
    {
        Assert.Equal("dotnet test exited with the code 1", first);
        _h.TestSeconds[Index(which)] = seconds;
        _h.RedBuilds[Index(which)] = (first, second);
    }

    [Given("the {word} test gate takes {int} seconds and is green")]
    public void GivenGreen(string which, int seconds) => _h.TestSeconds[Index(which)] = seconds;

    [Given("the first test gate is red with a Redo text of two lines, {string} and {string}")]
    public void GivenTwoLines(string first, string second)
    {
        Assert.Equal("dotnet test exited with the code 1", first);
        _h.RedBuilds[1] = (first, second);
    }

    [Given("the first review blocks the first build with the item {string}")]
    public void GivenBlocks(string item) => _h.BlockedBuilds[1] = item;

    [Given("the check exits with the code {int} after the first build")]
    public void GivenCheck(int code) => _h.CheckCodes[1] = code;

    [When("the run delivers with {int} Redo(s)")]
    public async Task WhenDelivers(int redos)
    {
        _result = await _h.RunAsync().ConfigureAwait(false);
        Assert.NotNull(_result.PullRequest);
        Assert.Equal(redos, _result.Redos);
    }

    [When("the run ends")]
    public async Task WhenEnds()
    {
        _result = await _h.RunAsync().ConfigureAwait(false);
        Assert.Null(_result.PullRequest);
    }

    [Then("the summary file ends with the lines")]
    public void ThenEnds(string expected) =>
        Assert.EndsWith(expected.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", _h.Summary, StringComparison.Ordinal);

    [Then("the row {string} has the seconds {int}")]
    public void ThenSeconds(string name, int seconds) => Assert.Equal(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), Cells(Row(name))[1]);

    [Then("the row {string} has the result {string}")]
    public void ThenResult(string name, string result) => Assert.Equal(result, Cells(Row(name))[4]);

    [Then("the Redos list holds the item {string}")]
    public void ThenItem(string item) => Assert.Contains(item, Lines);

    [Then("the summary does not hold {string}")]
    public void ThenNotHold(string text) => Assert.DoesNotContain(text, _h.Summary, StringComparison.Ordinal);

    [Then("the summary holds the row {string}")]
    public void ThenRow(string row) => Assert.Contains(row, Lines);

    [Then("the summary rows are in this order")]
    public void ThenOrder(Table table) =>
        Assert.Equal(
            [.. table.Rows.Select(row => row["row"])],
            [.. Lines.Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Skip(1).Select(line => Cells(line)[0])]);

    [Then("the summary file starts with the text {string}")]
    public void ThenStarts(string text) => Assert.StartsWith(text, _h.Summary, StringComparison.Ordinal);

    [Then("the summary file holds the summary after that text")]
    public void ThenAfter()
    {
        Assert.True(_h.Summary.IndexOf("## Factory run", StringComparison.Ordinal) > "earlier step text".Length - 1);
        Assert.EndsWith("\n", _h.Summary, StringComparison.Ordinal);
    }
}
