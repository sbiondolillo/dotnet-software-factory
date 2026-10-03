using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class StarterAgentsSteps(StarterState state)
{
    // The compare trims trailing line feeds on both sides.
    [Then("the copy's AGENTS.md at its root holds this text")]
    public void ThenTheCopysAgentsHolds(string docString)
        => StarterChecks.TextEquals(Path.Combine(state.Copy, "AGENTS.md"), docString);

    [Then("the comment in the copy's {string} starts with the line {string}")]
    public void ThenTheCommentStarts(string file, string line)
        => StarterChecks.CommentStarts(Path.Combine(state.Copy, file), line);

    [Then("the comment in the copy's {string} does not hold the line {string}")]
    public void ThenTheCommentLacks(string file, string line)
        => StarterChecks.CommentLacks(Path.Combine(state.Copy, file), line);
}
