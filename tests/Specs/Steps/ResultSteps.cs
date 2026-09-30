using Reqnroll;
using Xunit;

namespace Specs.Steps;

/// <summary>The verdict of the run a scenario made, set by the step class of the feature.</summary>
public sealed class ResultState
{
    public Func<(bool Passed, string Detail)>? Outcome { get; set; }
}

[Binding]
public sealed class ResultSteps(ResultState state)
{
    [Then("the result is passed")]
    public void ThenPassed()
    {
        (bool passed, string detail) = state.Outcome!();
        Assert.True(passed, detail);
    }

    [Then("the result is failed")]
    public void ThenFailed() => Assert.False(state.Outcome!().Passed);
}
