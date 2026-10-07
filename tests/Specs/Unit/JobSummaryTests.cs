using Core;
using Xunit;

namespace Specs.Unit;

public sealed class JobSummaryTests
{
    private static string[] Lines(JobSummaryHarness h) => h.Summary.Split('\n');

    [Fact]
    public async Task AnUnsetPathWritesNothingAndTheRunBehavesAsBefore()
    {
        using var h = new JobSummaryHarness { SummaryPath = null };
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result.PullRequest);
        Assert.Equal("", Directory.GetFiles(h.FilesDirectory).Select(File.ReadAllText).Single());
    }

    [Fact]
    public async Task AnEmptyPathWritesNothing()
    {
        using var h = new JobSummaryHarness { SummaryPath = "" };
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result.PullRequest);
        Assert.Equal("", Directory.GetFiles(h.FilesDirectory).Select(File.ReadAllText).Single());
    }

    [Fact]
    public async Task AFailedPlanGivesThePlanRowAloneAndNoRedosHeading()
    {
        using var h = new JobSummaryHarness();
        h.PlanAnswers.Add(new SummaryAnswer(7, "0.4213", 31.9, IsError: true, Text: "boom"));
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "## Factory run\n\n| phase | seconds | turns | spend | result |\n|---|---|---|---|---|\n| plan | 31 | 7 | $0.4213 | failed |\n",
            h.Summary);
        Assert.DoesNotContain("### Redos", h.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedTestGateFailsItsRowAndListsEachTextInOrder()
    {
        using var h = new JobSummaryHarness();
        h.RedBuilds[1] = ("dotnet test exited with the code 1", "failed test: Specs.Unit.CheckoutTests.AddsTwoPrices: Values differ");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("| test 1 | 0 | 0 | $0 | failed |", Lines(h));
        string[] items = [.. Lines(h).Where(line => line.StartsWith("- Redo", StringComparison.Ordinal))];
        Assert.Equal(
            ["- Redo 1, after test 1: dotnet test exited with the code 1", "- Redo 1, after test 1: failed test: Specs.Unit.CheckoutTests.AddsTwoPrices: Values differ"],
            items);
    }

    [Fact]
    public async Task ABlockingReviewListsEachItemAfterTheReviewRow()
    {
        using var h = new JobSummaryHarness();
        h.BlockedBuilds[1] = "First fault.";
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("| review 1 | 0 | 1 | $0 | blocked |", Lines(h));
        Assert.Contains("- Redo 1, after review 1: First fault.", Lines(h));
        Assert.Contains("| review 2 | 0 | 1 | $0 | approved |", Lines(h));
    }

    [Theory]
    [InlineData(1, "red")]
    [InlineData(0, "green")]
    public async Task ACheckIsGreenOnTheCodeZeroAndRedOtherwise(int code, string result)
    {
        using var h = new JobSummaryHarness();
        h.Labels.Add("cleanup");
        h.Body = JobSummaryHarness.CleanupBody;
        h.CheckCodes[1] = code;
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains($"| check 1 | 0 | 0 | $0 | {result} |", Lines(h));
    }

    [Fact]
    public async Task AFailedBuildEndsTheSummaryWithItsFailedRow()
    {
        using var h = new JobSummaryHarness();
        h.BuildAnswers[1] = new SummaryAnswer(3, "0.5000", 10, IsError: true, Text: "stuck");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Null(result.PullRequest);
        Assert.Equal("| build 1 | 10 | 3 | $0.5000 | failed |", Lines(h)[^2]);
    }

    [Fact]
    public async Task AReviewErrorEndsTheRunWithAReviewRowThatIsNotApproved()
    {
        using var h = new JobSummaryHarness();
        h.ReviewAnswers[1] = new SummaryAnswer(2, "0.1000", 5, IsError: true, Text: "api error");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Null(result.PullRequest);
        Assert.Contains("| review 1 | 5 | 2 | $0.1000 | failed |", Lines(h));
        Assert.DoesNotContain("approved", h.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpentBudgetListsFourBuildsAndThreeRedos()
    {
        using var h = new JobSummaryHarness();
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            h.RedBuilds[build] = ("dotnet test exited with the code 1", $"output {build}");
        }

        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Null(result.PullRequest);
        Assert.Equal(3, result.Redos);
        string[] lines = Lines(h);
        Assert.Equal(4, lines.Count(line => line.StartsWith("| build ", StringComparison.Ordinal)));
        Assert.Equal(4, lines.Count(line => line.StartsWith("| test ", StringComparison.Ordinal)));
        Assert.Equal(3, lines.Count(line => line.StartsWith("- Redo ", StringComparison.Ordinal)));
        Assert.Contains("- Redo 3, after test 3: dotnet test exited with the code 1", lines);
    }

    [Fact]
    public async Task ARedoTextListsItsFirstLineOnly()
    {
        using var h = new JobSummaryHarness();
        h.RedBuilds[1] = ("dotnet test exited with the code 1", "hidden second line");
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("- Redo 1, after test 1: dotnet test exited with the code 1", Lines(h));
        Assert.DoesNotContain("hidden", h.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextWithALeadingEmptyLineOrOneLineIsHandled()
    {
        var summary = new JobSummary();
        summary.AddRedos(1, "test 1", ["\n\nsecond", "only", "", "\r\n"]);
        string text = summary.Render();
        Assert.Contains("- Redo 1, after test 1: second\n", text, StringComparison.Ordinal);
        Assert.Contains("- Redo 1, after test 1: only\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACostKeepsItsZerosAndAPartOfASecondIsDropped()
    {
        using var h = new JobSummaryHarness();
        h.BuildAnswers[1] = new SummaryAnswer(4, "0.9000", 31.9);
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains("| build 1 | 31 | 4 | $0.9000 | passed |", Lines(h));
    }

    [Theory]
    [InlineData("earlier\n", "earlier\n\n## Factory run")]
    [InlineData("earlier", "earlier\n\n## Factory run")]
    public async Task EarlierTextStaysAndTheSummaryFollowsIt(string earlier, string start)
    {
        using var h = new JobSummaryHarness();
        File.WriteAllText(h.SummaryPath!, earlier);
        await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith(start, h.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnwritablePathKeepsTheResultAndNamesTheFailureOnTheErrorStream()
    {
        using var h = new JobSummaryHarness();
        h.SummaryPath = Path.Combine(h.FilesDirectory, "absent", "summary.md");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result.PullRequest);
        Assert.Contains("summary: cannot write", h.Errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALabelRefusalBeforeTheCloneWritesNoSummary()
    {
        using var h = new JobSummaryHarness();
        h.Labels.Add("refactor");
        h.Labels.Add("behavior");
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.Null(result.PullRequest);
        Assert.Equal("", h.Summary);
    }

    [Fact]
    public async Task ARunThatThrowsAfterTheCloneStillWritesTheRowsThatRan()
    {
        using var h = new JobSummaryHarness { LintFails = true };
        await Assert.ThrowsAsync<LintGateException>(() => h.RunAsync(TestContext.Current.CancellationToken));
        Assert.Contains("| plan | 0 | 1 | $0 | passed |", Lines(h));
    }
}
