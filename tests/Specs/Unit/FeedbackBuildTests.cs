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

    private static FeedbackBuild Build(FakeAgent agent) => new(
        new BuildPhase(agent, RecipeRules.BuildText(Recipe.Behavior, RunKind.Feedback)),
        "Answer the review threads", "the body", "build-model", 60, [Thread(A), Thread(B)]);

    private FakeAgent Agent(AgentAnswer answer, Action onPrompt)
    {
        var agent = new FakeAgent(_tree.FullName);
        agent.Answer(answer);
        agent.Act(_ => onPrompt());
        return agent;
    }

    private void WriteReport(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportFile)!);
        File.WriteAllText(ReportFile, text);
    }

    [Fact]
    public async Task AnErrorFlagIsTheStopWithItsTextAndTheReportIsNotRead()
    {
        FakeAgent agent = Agent(FakeAgent.Error("API Error: 529 Overloaded"), () => WriteReport(Report((A, "fixed", "x"), (B, "fixed", "y"))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Equal("API Error: 529 Overloaded", outcome.Stop);
        Assert.Empty(outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task AReportWithNoFaultGivesNoRedoTextAndOneAnswerForEachThread()
    {
        FakeAgent agent = Agent(FakeAgent.Done("done"), () => WriteReport(Report((B, "disputed", "the name is right"), (A, "fixed", "fixed it"))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Null(outcome.Stop);
        Assert.Empty(outcome.RedoTexts);
        Assert.Equal([new ThreadAnswer(A, "fixed it"), new ThreadAnswer(B, "the name is right")], build.Answers);
    }

    [Fact]
    public async Task EachFaultOfTheReportIsARedoTextAndGivesNoAnswer()
    {
        FakeAgent agent = Agent(FakeAgent.Done("done"), () => WriteReport(Report((A, "done", " "))));
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
        FakeAgent agent = Agent(FakeAgent.Done("done"), () => WriteReport(Report((A, "fixed", "fixed it"))));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Equal([$"The report {FeedbackBuild.ReportPath} has no item for the thread {B}."], outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task TheBuildAsksForTheReportAndAnAbsentReportIsARedoText()
    {
        FakeAgent agent = Agent(FakeAgent.Done("done"), () => { });
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Equal([FeedbackBuild.ReportName], Assert.Single(agent.Requests).Outputs);
        Assert.Equal(Phase.Build, agent.Requests[0].Phase);
        Assert.Equal([$"The report {FeedbackBuild.ReportPath} is absent. Write it."], outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Theory]
    [InlineData("not json", "is not JSON. Write one JSON object with a threads list.")]
    [InlineData("[]", "is not one JSON object with a threads list.")]
    [InlineData("""{"threads":{}}""", "is not one JSON object with a threads list.")]
    public async Task AReportThatIsNotOneObjectWithAThreadsListIsARedoText(string text, string fault)
    {
        FakeAgent agent = Agent(FakeAgent.Done("done"), () => WriteReport(text));
        FeedbackBuild build = Build(agent);
        BuildOutcome outcome = await build.RunAsync([], Ct);
        Assert.Equal([$"The report {FeedbackBuild.ReportPath} {fault}"], outcome.RedoTexts);
        Assert.Empty(build.Answers);
    }

    [Fact]
    public async Task TheFaultsOfTheAnswerPassThroughOnAPassAndOnAStop()
    {
        var agent = new FakeAgent(_tree.FullName);
        agent.Answer(FakeAgent.Done() with { Faults = ["no commit"] }, 1);
        agent.Answer(FakeAgent.Error("API Error") with { Faults = ["no commit"] }, 2);
        agent.Stage(FeedbackBuild.ReportName, Report((A, "fixed", "x"), (B, "fixed", "y")));
        FeedbackBuild build = Build(agent);
        BuildOutcome passed = await build.RunAsync([], Ct);
        BuildOutcome stopped = await build.RunAsync([], Ct);
        Assert.Equal(["no commit"], passed.Faults);
        Assert.Empty(passed.RedoTexts);
        Assert.Equal("API Error", stopped.Stop);
        Assert.Equal(["no commit"], stopped.Faults);
    }

    [Fact]
    public async Task AFaultAfterACleanBuildClearsTheAnswersOfTheBuildBefore()
    {
        int builds = 0;
        FakeAgent agent = Agent(FakeAgent.Done("done"), () =>
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
}
