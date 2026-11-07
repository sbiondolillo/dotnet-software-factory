using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The job summary as a value: its rows, its lines, its Redo items and its file. The whole-run tests at the end are the
/// wiring of <see cref="FactoryRun"/> to its summary file, and <c>BuildLoopTests</c> holds the rows and the lines of the loop.</summary>
public sealed class JobSummaryTests
{
    private static string[] Lines(JobSummaryHarness h) => h.Summary.Split('\n');

    [Fact]
    public async Task ARowWritesItsResultLineWithThePhaseAndNoNumber()
    {
        using var progress = new StringWriter();
        var summary = new JobSummary(progress);
        string first = await summary.AddRowAsync("test", TimeSpan.Zero, "failed", TestContext.Current.CancellationToken);
        string second = await summary.AddRowAsync("test", TimeSpan.Zero, "passed", TestContext.Current.CancellationToken);
        Assert.Equal(("test 1", "test 2"), (first, second));
        Assert.Equal("test: failed\ntest: passed\n", progress.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARowWithNoWriterWritesNoLine()
    {
        var summary = new JobSummary();
        string name = await summary.AddRowAsync("plan", TimeSpan.Zero, "passed", TestContext.Current.CancellationToken);
        Assert.Equal("plan", name);
        Assert.Contains("| plan | 0 | 0 | $0 | passed |", summary.Render(FactoryRun.SummaryHeading), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledTokenAddsNoRowAndWritesNoLine()
    {
        using var progress = new StringWriter();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var summary = new JobSummary(progress);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => summary.AddRowAsync("plan", TimeSpan.Zero, "passed", source.Token));
        Assert.True(summary.IsEmpty);
        Assert.Equal("", progress.ToString());
    }

    [Fact]
    public async Task ARedoTextListsItsFirstLineOnly()
    {
        var summary = new JobSummary();
        string row = await summary.AddRowAsync("test", TimeSpan.Zero, "failed", TestContext.Current.CancellationToken);
        summary.AddRedos(1, row, ["dotnet test exited with the code 1\nhidden second line", "\n\nsecond", "only", "", "\r\n"]);
        string text = summary.Render(FactoryRun.SummaryHeading);
        Assert.Contains("- Redo 1, after test 1: dotnet test exited with the code 1\n", text, StringComparison.Ordinal);
        Assert.Contains("- Redo 1, after test 1: second\n", text, StringComparison.Ordinal);
        Assert.Contains("- Redo 1, after test 1: only\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACostKeepsItsZerosAndAPartOfASecondIsDropped()
    {
        var summary = new JobSummary();
        summary.Prompt(TimeSpan.FromSeconds(31.9), 4, 0.9000m);
        await summary.AddRowAsync("build", null, "passed", TestContext.Current.CancellationToken);
        Assert.Contains("| build 1 | 31 | 4 | $0.9000 | passed |", summary.Render(FactoryRun.SummaryHeading), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARowWithAnElapsedTimeStillTakesTheTurnsAndTheSpendOfTheAnswers()
    {
        var summary = new JobSummary();
        summary.Prompt(TimeSpan.FromSeconds(10), 2, 0.25m);
        await summary.AddRowAsync("build", TimeSpan.FromSeconds(3), "passed", TestContext.Current.CancellationToken);
        await summary.AddRowAsync("lint", TimeSpan.FromSeconds(1), "passed", TestContext.Current.CancellationToken);
        string text = summary.Render(FactoryRun.SummaryHeading);
        Assert.Contains("| build 1 | 3 | 2 | $0.25 | passed |", text, StringComparison.Ordinal);
        Assert.Contains("| lint 1 | 1 | 0 | $0 | passed |", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("earlier\n", "earlier\n\n## Factory run")]
    [InlineData("earlier", "earlier\n\n## Factory run")]
    [InlineData("", "## Factory run")]
    public async Task EarlierTextStaysAndTheSummaryFollowsIt(string earlier, string start)
    {
        using var files = new TempDirectory();
        string path = Path.Combine(files.Path, "summary.md");
        await File.WriteAllTextAsync(path, earlier, TestContext.Current.CancellationToken);
        var summary = new JobSummary();
        await summary.AddRowAsync("plan", TimeSpan.Zero, "passed", TestContext.Current.CancellationToken);
        await JobSummary.AppendToFileAsync(path, summary.Render(FactoryRun.SummaryHeading), TestContext.Current.CancellationToken);
        string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.StartsWith(start, text, StringComparison.Ordinal);
        Assert.EndsWith("| plan | 0 | 0 | $0 | passed |\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnUnsetPathWritesNothingAndTheRunBehavesAsBefore(string? path)
    {
        using var h = new JobSummaryHarness { SummaryPath = path };
        FactoryRunResult result = await h.RunAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result.PullRequest);
        Assert.Equal("", Directory.GetFiles(h.FilesDirectory).Select(File.ReadAllText).Single());
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
        Assert.Equal(["plan", "build 1"], Lines(h).Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Skip(1).Select(line => line.Split('|')[1].Trim()));
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("job-summary-file-");

        public string Path => _directory.FullName;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
