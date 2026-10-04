using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class ReviewFeedbackWorkflowSteps(StarterState state)
{
    private static string Section(string heading) => WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), heading);

    [Then("the copy's workflow {string} holds this text")]
    public void ThenTheWorkflowHoldsThisText(string name, string text)
        => StarterChecks.TextEquals(Path.Combine(state.Copy, ".github", "workflows", name), text);

    [Then("the README section {string} holds the line {string}")]
    public static void ThenReadmeHoldsLine(string heading, string line) => WorkflowChecks.HoldsLine(Section(heading), line);

    [Then("the README section {string} does not hold the line {string}")]
    public static void ThenReadmeHoldsNoLine(string heading, string line) => WorkflowChecks.HoldsNoLine(Section(heading), line);

    [Then("the README section {string} holds the line {string} after the line about the label {string}")]
    public static void ThenReadmeHoldsLineAfter(string heading, string line, string label)
        => WorkflowChecks.HoldsLineAfterLabel(Section(heading), line, label);
}
