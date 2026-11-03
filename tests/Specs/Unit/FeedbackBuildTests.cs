using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeedbackBuildTests : IDisposable
{
    private const string A = "PRRT_a";
    private const string B = "PRRT_b";
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("feedback-build-");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ReportFile => Path.Combine(_tree.FullName, ".factory", "threads.json");

    public void Dispose() => _tree.Delete(recursive: true);

    private static ReviewThread Thread(string id) => new(id, false, "src/Core/Checkout.cs", 1, []);

    private static string Report(params (string Id, string Status, string Reply)[] items) =>
        FeedbackRunHarness.Items(items);

    private FeedbackBuild Build(FakeAgent agent) => new(
        new BuildPhase(agent, RecipeRules.BuildText(Recipe.Behavior, RunKind.Feedback)),
        "Answer the review threads", "the body", "build-model", 60, _tree.FullName, [Thread(A), Thread(B)]);

    private void WriteReport(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportFile)!);
        File.WriteAllText(ReportFile, text);
    }

    [Fact]
    public async Task AnErrorFlagIsTheStopWithItsTextAndTheReportIsNotRead()
    {
        var agent = new FakeAgent(new ClaudeCodeResult("API Error: 529 Overloaded", 1, 0m, true), () => WriteReport(Report((A, "fixed", "x"), (B, "fixed", "y"))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Equal("API Error: 529 Overloaded", outcome.Stop);
        Assert.Empty(outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task AReportWithNoFaultGivesNoRedoTextAndOneAnswerForEachThread()
    {
        var agent = new FakeAgent(new ClaudeCodeResult("done", 1, 0m, false), () => WriteReport(Report((B, "disputed", "the name is right"), (A, "fixed", "fixed it"))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Null(outcome.Stop);
        Assert.Empty(outcome.RedoTexts);
        Assert.Equal([new ThreadAnswer(A, "fixed it"), new ThreadAnswer(B, "the name is right")], build.Answers);
    }

    [Fact]
    public async Task EachFaultOfTheReportIsARedoTextAndGivesNoAnswer()
    {
        var agent = new FakeAgent(new ClaudeCodeResult("done", 1, 0m, false), () => WriteReport(Report((A, "done", " "))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Null(outcome.Stop);
        Assert.Equal(
            [
                $"The item of the thread {A} has the status done. The status is fixed or disputed.",
                $"The item of the thread {A} has no reply. The reply holds a character other than whitespace.",
                $"The report {FeedbackBuild.ReportPath} has no item for the thread {B}.",
            ],
            outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task AReportWithAGoodItemAndAFaultGivesNoAnswer()
    {
        var agent = new FakeAgent(new ClaudeCodeResult("done", 1, 0m, false), () => WriteReport(Report((A, "fixed", "fixed it"))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Equal([$"The report {FeedbackBuild.ReportPath} has no item for the thread {B}."], outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task TheBuildDeletesTheReportOfTheBuildBeforeAndAnAbsentReportIsARedoText()
    {
        WriteReport(Report((A, "fixed", "x"), (B, "fixed", "y")));
        bool seen = true;
        var agent = new FakeAgent(new ClaudeCodeResult("done", 1, 0m, false), () => seen = File.Exists(ReportFile));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.False(seen);
        Assert.Equal([$"The report {FeedbackBuild.ReportPath} is absent. Write it."], outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task AFaultAfterACleanBuildClearsTheAnswersOfTheBuildBefore()
    {
        int builds = 0;
        var agent = new FakeAgent(new ClaudeCodeResult("done", 1, 0m, false), () =>
        {
            builds++;
            if (builds is 1)
            {
                WriteReport(Report((A, "fixed", "x"), (B, "fixed", "y")));
            }
        });
        FeedbackBuild build = Build(agent);
        await build.RunAsync([], Ct);
        Assert.Equal(2, build.Answers.Count);
        BuildOutcome second = await build.RunAsync(["The total is wrong."], Ct);
        Assert.Single(second.RedoTexts);
        Assert.Empty(build.Answers);
        Assert.Contains("The total is wrong.", agent.Prompts[1], StringComparison.Ordinal);
    }

    private sealed class FakeAgent(ClaudeCodeResult answer, Action onPrompt) : Agent
    {
        public List<string> Prompts { get; } = [];

        public override Task<ClaudeCodeResult> RunAsync(string prompt, string model, int maxTurns, string phase, CancellationToken cancellationToken = default)
        {
            Prompts.Add(prompt);
            onPrompt();
            return Task.FromResult(answer);
        }
    }
}
