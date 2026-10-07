using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class StarterIssueTemplatesSteps(StarterState state)
{
    [Then("the copy's directory {string} holds the files {string}, {string} and {string} and no other file")]
    public void ThenTheCopysDirectoryHolds(string directory, string first, string second, string third)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, directory), [first, second, third]);
}
