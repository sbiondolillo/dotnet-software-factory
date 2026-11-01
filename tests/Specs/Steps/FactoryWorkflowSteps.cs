using System.Text.Json;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FactoryWorkflowSteps
{
    private WorkflowStepRun? _run;

    [When("the step {string} of {string} runs on the lockfile {string} of this repo")]
    public async Task WhenTheStepRunsOnTheLockfileOfThisRepo(string stepName, string workflow, string lockfile)
    {
        string root = StarterChecks.RepoRoot();
        string text = WorkflowStep.RunText(Path.Combine(root, workflow), stepName);
        _run = await WorkflowStep.RunAsync(text, await File.ReadAllTextAsync(Path.Combine(root, lockfile))).ConfigureAwait(false);
    }

    [When("the step {string} of {string} runs on a lockfile that holds only the agent {string} with the id {string}")]
    public async Task WhenTheStepRunsOnALockfileThatHoldsOnlyTheAgent(string stepName, string workflow, string key, string id)
    {
        string text = WorkflowStep.RunText(Path.Combine(StarterChecks.RepoRoot(), workflow), stepName);
        string lockfile = JsonSerializer.Serialize(new { resources = new Dictionary<string, object> { [key] = new { kind = "agent", id } } });
        _run = await WorkflowStep.RunAsync(text, lockfile).ConfigureAwait(false);
    }

    [Then("the step exits with the code {int}")]
    public void ThenTheStepExitsWithTheCode(int code) => Assert.Equal(code, Run.ExitCode);

    [Then("the job environment file holds the line {string}")]
    public void ThenTheJobEnvironmentFileHoldsTheLine(string line)
        => Assert.Contains(line, Run.EnvironmentFile.Split('\n'));

    [Then("the error output of the step holds {string}")]
    public void ThenTheErrorOutputOfTheStepHolds(string text)
        => Assert.Contains(text, Run.StandardError, StringComparison.Ordinal);

    private WorkflowStepRun Run => _run ?? throw new InvalidOperationException("no step ran");
}
