using Core;
using Xunit;

namespace Specs.Unit;

public class JobSummaryRoundingTests
{
    [Fact]
    public void ARowRoundsTheSumOfItsAnswersToFourDecimalPlaces()
    {
        var summary = new JobSummary();
        summary.Prompt(TimeSpan.FromSeconds(1), 1, 0.07597370000000002m);
        summary.Prompt(TimeSpan.FromSeconds(1), 1, 0.00002m);
        summary.AddRow("plan", null, "passed");
        Assert.Contains("| plan | 2 | 2 | $0.0760 | passed |", summary.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARowWithNoAnswerStatesZero()
    {
        var summary = new JobSummary();
        summary.AddRow("test", TimeSpan.Zero, "passed");
        Assert.Contains("| 0 | $0 |", summary.Render(), StringComparison.Ordinal);
    }
}
