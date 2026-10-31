using Xunit;

namespace Specs.Unit;

public sealed class FactoryWorkflowTests
{
    private const string StoreStep = "Push the transcripts to the store";

    [Fact]
    public void TheRunCommandAndThenTheStoreStepEndTheJob()
        => WorkflowChecks.LastTwoSteps(
            RootWorkflow(),
            "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --skip-integration on --copilot-review ${{ inputs.copilot || 'off' }} --agent managed",
            StoreStep,
            "always()");

    [Fact]
    public void TheStoreStepIsTheStoreStepOfTheStarter()
        => Assert.Equal(
            StoreStepText(Path.Combine(StarterChecks.RepoRoot(), "templates", "starter", ".github", "workflows", "factory.yml")),
            StoreStepText(RootWorkflow()));

    private static string RootWorkflow() => Path.Combine(StarterChecks.RepoRoot(), ".github", "workflows", "factory.yml");

    /// <summary>The text of the store step: from its name line to the end of the file, since it is the last step.</summary>
    private static string StoreStepText(string workflow)
    {
        string text = File.ReadAllText(workflow);
        int start = text.IndexOf("      - name: " + StoreStep, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{workflow} has no step {StoreStep}");
        return text[start..];
    }
}
