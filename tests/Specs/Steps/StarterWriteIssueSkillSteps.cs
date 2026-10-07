using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class StarterWriteIssueSkillSteps(StarterState state)
{
    [Then("the copy's directory {string} holds exactly the files {string} and {string}")]
    public void ThenTheDirectoryHoldsExactly(string directory, string first, string second)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, directory), [first, second]);

    [Then("the copy's directory {string} holds no file besides {string} and {string}")]
    public void ThenTheDirectoryHoldsNoFileBesides(string directory, string first, string second)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, directory), [first, second]);
}
