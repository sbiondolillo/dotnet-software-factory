using System.Text.Json;
using Xunit;

namespace Specs.Unit;

public sealed class FactoryWorkflowTests
{
    private const string StoreStep = "Push the transcripts to the store";
    private const string RunStep = "Run the factory";
    private const string LockStep = "Read the builder's ids from the lockfile";

    [Fact]
    public void TheRunCommandAndThenTheStoreStepEndTheJob()
        => WorkflowChecks.LastTwoSteps(
            RootWorkflow(),
            "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --skip-integration on --copilot-review ${{ inputs.copilot || 'off' }} --agent managed $FACTORY_BASE_ARGS",
            StoreStep,
            "always()");

    [Fact]
    public void TheStoreStepIsTheStoreStepOfTheStarter()
        => Assert.Equal(
            StoreStepText(Path.Combine(StarterChecks.RepoRoot(), "templates", "starter", ".github", "workflows", "factory.yml")),
            StoreStepText(RootWorkflow()));

    [Theory]
    [InlineData("ANTHROPIC_API_KEY", "${{ secrets.JUDGE_ANTHROPIC_API_KEY }}")]
    [InlineData("FACTORY_APP_PRIVATE_KEY", "${{ secrets.DSF_REPAIR_APP_PRIVATE_KEY }}")]
    [InlineData("FACTORY_APP_ID", "${{ vars.DSF_REPAIR_APP_ID }}")]
    [InlineData("FACTORY_APP_INSTALLATION_ID", "${{ vars.DSF_REPAIR_APP_INSTALLATION_ID }}")]
    public void TheRunStepMapsTheNameToTheValue(string name, string value)
    {
        Dictionary<string, object?> env = Step(RunStep).GetValueOrDefault("env") as Dictionary<string, object?> ?? [];

        Assert.Equal(value, env.GetValueOrDefault(name) as string);
    }

    [Fact]
    public void AStepBeforeTheRunStepReadsTheIdsFromTheLockfileIntoTheJobEnvironment()
    {
        List<Dictionary<string, object?>> steps = Steps();
        int lockIndex = steps.FindIndex(step => (step.GetValueOrDefault("name") as string) == LockStep);
        int runIndex = steps.FindIndex(step => (step.GetValueOrDefault("name") as string) == RunStep);
        string run = steps[lockIndex].GetValueOrDefault("run") as string ?? "";

        Assert.InRange(lockIndex, 0, runIndex - 1);
        Assert.StartsWith("jq ", run, StringComparison.Ordinal);
        Assert.Contains(" .github/agents/claude-lock.json ", run, StringComparison.Ordinal);
        Assert.EndsWith(">> \"$GITHUB_ENV\"", run, StringComparison.Ordinal);
        Assert.Contains("\"FACTORY_AGENT_ID=\"", run, StringComparison.Ordinal);
        Assert.Contains("\"FACTORY_ENVIRONMENT_ID=\"", run, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("./builder.environment.yaml")]
    [InlineData("./builder.md")]
    public void TheLockStepReadsAKeyThatTheLockfileHoldsWithAnId(string key)
    {
        string run = Step(LockStep).GetValueOrDefault("run") as string ?? "";
        string lockfile = Path.Combine(StarterChecks.RepoRoot(), ".github", "agents", "claude-lock.json");
        using var document = JsonDocument.Parse(File.ReadAllText(lockfile));
        string? id = document.RootElement.TryGetProperty("resources", out JsonElement resources)
            && resources.TryGetProperty(key, out JsonElement resource)
            && resource.TryGetProperty("id", out JsonElement idElement)
            && idElement.ValueKind is JsonValueKind.String
            ? idElement.GetString()
            : null;

        Assert.Contains($".resources[\"{key}\"].id", run, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(id), $"the lockfile holds no non-empty id under resources for {key}");
    }

    private static List<Dictionary<string, object?>> Steps()
    {
        Dictionary<string, object?> root = WorkflowChecks.Parse(File.ReadAllText(RootWorkflow()));
        var job = (root.GetValueOrDefault("jobs") as Dictionary<string, object?>)?.GetValueOrDefault("factory") as Dictionary<string, object?>;
        return job?.GetValueOrDefault("steps") is List<object?> list ? [.. list.OfType<Dictionary<string, object?>>()] : [];
    }

    private static Dictionary<string, object?> Step(string name)
        => Steps().Find(step => (step.GetValueOrDefault("name") as string) == name) ?? throw new InvalidOperationException($"the factory workflow has no step {name}");

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
