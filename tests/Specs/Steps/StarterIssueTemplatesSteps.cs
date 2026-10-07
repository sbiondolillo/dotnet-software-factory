using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class StarterIssueTemplatesSteps(StarterState state)
{
    [Then("the copy's directory {string} holds the files {string}, {string} and {string} and no other file")]
    public void ThenTheCopysDirectoryHolds(string directory, string first, string second, string third)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, directory), [first, second, third]);

    [Then("the copy's directory {string} holds the files {string} and {string} and no other file")]
    public void ThenTheCopysDirectoryHoldsTwo(string directory, string first, string second)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, directory), [first, second]);

    [Then("the copy's file {string}, in {string}, holds this paragraph in place of the paragraph that starts {string}")]
    public void ThenTheCopysFileHoldsParagraph(string file, string heading, string start, string docString)
        => StarterChecks.SectionHoldsParagraph(Path.Combine(state.Copy, file), heading, start, docString);
}
