using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class ReviewFeedbackWorkflowSteps(StarterState state)
{
    [Then("the copy's workflow {string} holds this text")]
    public void ThenTheWorkflowHoldsThisText(string name, string text)
        => StarterChecks.TextEquals(Path.Combine(state.Copy, ".github", "workflows", name), text);
}
