using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ChecksTokenSteps(ConsoleProgramSteps console)
{
    [Given("the environment holds {string} set to {string} and {string}")]
    public void GivenEnvironmentHoldsTwo(string name, string value, string key)
    {
        console.Harness.Environment[name] = value;
        Assert.True(console.Harness.Environment.ContainsKey(key));
    }

    [Given("the environment holds {string} set to {string}")]
    public void GivenEnvironmentHolds(string name, string value) => console.Harness.Environment[name] = value;

    [Then("every read of the check runs holds the header {string} with the value {string}")]
    public void ThenCheckReadsHold(string header, string value) =>
        WorkflowChecks.HoldsHeader(console.Harness.Requests.Where(IsCheckRead), header, value);

    [Then("every other request to GitHub's API holds the header {string} with the value {string}")]
    public void ThenOthersHold(string header, string value) =>
        WorkflowChecks.HoldsHeader(console.Harness.Requests.Where(r => !IsCheckRead(r)), header, value);

    [Then("the README section {string} holds the line {string} after the line that starts {string}")]
    public static void ThenReadmeAfterStart(string heading, string line, string prefix) =>
        WorkflowChecks.HoldsLineAfterStart(WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), heading), line, prefix);

    private static bool IsCheckRead(TimedRequest r) => r.Path.EndsWith("/check-runs", StringComparison.Ordinal);
}
