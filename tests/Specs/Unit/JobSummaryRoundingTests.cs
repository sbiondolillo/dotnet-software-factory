using Core;
using Xunit;

namespace Specs.Unit;

public class JobSummaryRoundingTests
{
    [Fact]
    public async Task ARowRoundsTheSumOfItsAnswersToFourDecimalPlaces()
    {
        var summary = new JobSummary();
        summary.Prompt(TimeSpan.FromSeconds(1), 1, 0.07597370000000002m);
        summary.Prompt(TimeSpan.FromSeconds(1), 1, 0.00002m);
        await summary.AddRowAsync("plan", null, "passed", TestContext.Current.CancellationToken);
        Assert.Contains("| plan | 2 | 2 | $0.0760 | passed |", summary.Render(FactoryRun.SummaryHeading), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARowWithNoAnswerStatesZero()
    {
        var summary = new JobSummary();
        await summary.AddRowAsync("test", TimeSpan.Zero, "passed", TestContext.Current.CancellationToken);
        Assert.Contains("| 0 | $0 |", summary.Render(FactoryRun.SummaryHeading), StringComparison.Ordinal);
    }
}
