using Core;
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
            "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --skip-integration on --copilot-review ${{ inputs.copilot || 'off' }} --agent managed",
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

    [Fact]
    public async Task TheLockfileStepWritesTheTwoIdsOfTheBuilderToGitHubEnv()
    {
        Assert.SkipWhen(FindOnPath("jq") is null, "jq is not on PATH; the step's text is pinned by the tests above.");
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-lockstep-");
        try
        {
            string agents = Directory.CreateDirectory(Path.Combine(directory.FullName, ".github", "agents")).FullName;
            File.WriteAllText(Path.Combine(agents, "claude-lock.json"), """
                {"version": 1, "resources": {
                  "./judge.md": {"kind": "agent", "id": "agent_judge"},
                  "./builder.md": {"kind": "agent", "id": "agent_builder", "version": "3"},
                  "./builder.environment.yaml": {"kind": "environment", "id": "env_builder"}}}
                """);
            string githubEnv = Path.Combine(directory.FullName, "github-env");
            File.WriteAllText(githubEnv, "EARLIER=1\n");
            var spec = new CommandSpec
            {
                Command = "sh",
                Arguments = ["-c", Step(LockStep).GetValueOrDefault("run") as string ?? ""],
                WorkingDirectory = directory.FullName,
                EnvironmentVariables = new Dictionary<string, string> { ["GITHUB_ENV"] = githubEnv },
            };

            CommandResult result = await new CommandRunner().RunAsync(spec, TestContext.Current.CancellationToken);

            Assert.True(result.ExitCode is 0, result.StandardError);
            Assert.Equal(["EARLIER=1", "FACTORY_AGENT_ID=agent_builder", "FACTORY_ENVIRONMENT_ID=env_builder"], File.ReadAllLines(githubEnv));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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

    private static string? FindOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, program))
            .FirstOrDefault(File.Exists);
}
