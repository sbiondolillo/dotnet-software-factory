using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class DeliverBodySteps(DeliverBodyState state)
{
    private static string Feature(params (string Title, string Then)[] scenarios) =>
        "Feature: A feature\n" + string.Concat(scenarios.Select(s => $"\n  Scenario: {s.Title}\n    Given a cart\n    Then {s.Then}\n"));

    private static string Text(string first, string? second, string? changed)
    {
        List<(string, string)> scenarios = [(first, first == changed ? "the total is 1" : "the total is 0")];
        if (second is not null)
        {
            scenarios.Add((second, second == changed ? "the total is 1" : "the total is 0"));
        }

        return Feature([.. scenarios]);
    }

    private IReadOnlyList<string> Section(string name)
    {
        string[] lines = state.Body.Split('\n');
        int start = Array.IndexOf(lines, $"## {name}");
        Assert.True(start >= 0, $"the body has no section {name}");
        return [.. lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal) && !l.StartsWith("Redos:", StringComparison.Ordinal)).Where(l => l.Length > 0)];
    }

    private static int Index(string word) => word switch
    {
        "first" => 1,
        "second" => 2,
        _ => throw new ArgumentException($"unknown ordinal {word}", nameof(word)),
    };

    [Given("the run of the issue {int} delivers with {int} Redo(s)")]
    public void GivenRun(int issue, int redos)
    {
        Assert.Equal(12, issue);
        state.ExpectedRedos = redos;
        _ = state.Harness;
    }

    [Given("the plan answer has the cost {string} and comes {double} seconds after its prompt")]
    public void GivenPlan(string cost, double seconds) => state.Harness.PlanAnswers.Add(new SummaryAnswer(1, cost, seconds));

    [Given("the {word} build answer has the cost {string} and comes {double} seconds after its prompt")]
    public void GivenBuild(string which, string cost, double seconds) => state.Harness.BuildAnswers[Index(which)] = new SummaryAnswer(1, cost, seconds);

    [Given("the review answer has the cost {string} and comes {double} seconds after its prompt")]
    public void GivenReview(string cost, double seconds) => state.Harness.ReviewAnswers[1] = new SummaryAnswer(1, cost, seconds);

    [Given("the first test gate is red and takes {int} seconds")]
    public void GivenRedGate(int seconds)
    {
        state.Harness.RedBuilds[1] = ("failed", "Assert.Equal() Failure: Values differ");
        state.Harness.TestSeconds[1] = seconds;
    }

    [Given("the second test gate passes and takes {int} seconds")]
    public void GivenSecondGate(int seconds) => state.Harness.TestSeconds[2] = seconds;

    [Given("the TRX files of the last test gate hold {int} passed tests")]
    public void GivenTests(int count) => state.Harness.ExtraPassedTests = count - 1;

    [Given("at the base commit the file {string} holds the scenarios {string}")]
    public void GivenBaseOne(string path, string first) => state.Harness.BaseFiles[path] = Text(first, null, null);

    [Given("at the base commit the file {string} holds the scenarios {string} and {string}")]
    public void GivenBaseTwo(string path, string first, string second) => state.Harness.BaseFiles[path] = Text(first, second, null);

    [Given("in the work tree the file {string} holds the scenarios {string}")]
    public void GivenWorkOne(string path, string first) => state.Harness.WorkFiles[path] = Text(first, null, null);

    [Given("in the work tree the file {string} holds the scenarios {string} and {string}")]
    public void GivenWorkTwo(string path, string first, string second) => state.Harness.WorkFiles[path] = Text(first, second, null);

    [Given("in the work tree the file {string} holds the scenarios {string} and {string}, with other steps in {string}")]
    public void GivenWorkChanged(string path, string first, string second, string changed) => state.Harness.WorkFiles[path] = Text(first, second, changed);

    [Given("the base commit holds no file {string}")]
    public void GivenBaseLacks(string path) => Assert.False(state.Harness.BaseFiles.ContainsKey(path));

    [Given("the work tree holds no file {string}")]
    public void GivenWorkLacks(string path) => state.Harness.WorkFiles[path] = null;

    /// <summary>The step "the issue {int} has the label {string}" binds in CleanupRunSteps, which hands a label of this run's issue here.</summary>
    public static void AddLabel(DeliverBodyState state, string label)
    {
        state.Harness.Labels.Add(label);
        if (label is "cleanup")
        {
            state.Harness.Body = JobSummaryHarness.CleanupBody;
        }
    }

    [Given("the issue {int} has the label {string} and a {string} section")]
    public void GivenLabelCheck(int issue, string label, string heading)
    {
        _ = issue;
        Assert.Equal("## Check", heading);
        AddLabel(state, label);
        state.Harness.Body = JobSummaryHarness.CleanupBody;
    }

    [Given("the issue {int} has the label {string} and no {string} section")]
    public void GivenLabelNoCheck(int issue, string label, string heading)
    {
        _ = issue;
        Assert.Equal("## Check", heading);
        AddLabel(state, label);
        state.Harness.Body = "Move the checkout total into its own class.";
    }

    [Given("the console program run delivers a pull request")]
    public async Task GivenConsoleRun()
    {
        using var console = new ConsoleHarness();
        int exit = await console.RunAsync(["run", "--repo", "sbiondolillo/factory-scratch", "--issue", "12", "--timeout", "0"], TestContext.Current.CancellationToken);
        Assert.Equal(0, exit);
        state.Body = System.Text.Json.JsonDocument.Parse(Assert.Single(console.Posts("/pulls")).Content!).RootElement.GetProperty("body").GetString() ?? "";
        using var version = new ConsoleHarness();
        Assert.Equal(0, await version.RunAsync(["--version"], TestContext.Current.CancellationToken));
        ConsoleVersion = version.Out.ToString().Trim();
    }

    private string ConsoleVersion { get; set; } = "";

    [When("the body of the pull request is read")]
    public void WhenRead() => Assert.NotEqual("", state.Body);

    [Then("the body of the pull request starts with the text")]
    public void ThenStarts(string text) => Assert.StartsWith(text.Replace("\r\n", "\n", StringComparison.Ordinal), state.Body, StringComparison.Ordinal);

    [Then("the last line of the body of the pull request is {string}")]
    public void ThenLast(string line) => Assert.Equal(line, state.Body.Split('\n')[^1]);

    [Then("the line before the line starting {string} is {string} followed by the text that the same build of the program prints for --version")]
    public void ThenVersion(string start, string lead)
    {
        string[] lines = state.Body.Split('\n');
        int total = Array.FindIndex(lines, l => l.StartsWith(start, StringComparison.Ordinal));
        Assert.True(total > 1);
        Assert.NotEqual("", ConsoleVersion);
        Assert.Equal(lead + ConsoleVersion, lines[total - 1]);
        Assert.Equal("", lines[total - 2]);
    }

    [Then("the section Scenarios of the body holds a line for {string} that says {word} {string}")]
    public void ThenScenarioLine(string path, string kind, string title) => ThenScenarioLine(path, kind, [title]);

    [Then("the section Scenarios of the body holds a line for {string} that says {word} {string} and {string}")]
    public void ThenScenarioLineTwo(string path, string kind, string first, string second) => ThenScenarioLine(path, kind, [first, second]);

    private void ThenScenarioLine(string path, string kind, string[] titles)
    {
        string line = Assert.Single(Section("Scenarios"), l => l.StartsWith($"- `{path}`:", StringComparison.Ordinal));
        Assert.Contains($"{kind} {string.Join(", ", titles.Select(t => $"\"{t}\""))}", line, StringComparison.Ordinal);
    }

    [Then("the section Scenarios of the body holds the one line {string}")]
    public void ThenOneLine(string line) => Assert.Equal([line], Section("Scenarios"));

    [Then("the section Gates of the body holds the line {string} between the line of the test gate and the line of the review")]
    public void ThenBetween(string line)
    {
        IReadOnlyList<string> gates = Section("Gates");
        int test = gates.ToList().FindIndex(l => l.StartsWith("- test:", StringComparison.Ordinal));
        int at = gates.ToList().IndexOf(line);
        int review = gates.ToList().FindIndex(l => l.StartsWith("- review:", StringComparison.Ordinal));
        Assert.True(test >= 0 && test < at && at < review, string.Join('|', gates));
    }

    [Then("the section Gates of the body does not hold the line {string}")]
    public void ThenNotHold(string line) => Assert.DoesNotContain(line, Section("Gates"));
}
