using Xunit;

namespace Specs.Unit;

public sealed class WorkflowStepTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workflow-step-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string Workflow(string steps)
    {
        string path = Path.Combine(_root, "w.yml");
        File.WriteAllText(path, $"name: w\non: push\njobs:\n  a:\n    runs-on: ubuntu-latest\n    steps:\n{steps}");
        return path;
    }

    [Fact]
    public void RunTextIsTheRunCommandOfTheNamedStep()
    {
        string path = Workflow("      - name: One\n        run: echo one\n      - name: Two\n        run: echo two\n");

        Assert.Equal("echo two", WorkflowStep.RunText(path, "Two"));
    }

    [Fact]
    public void RunTextThrowsForAStepThatTheWorkflowLacks()
    {
        string path = Workflow("      - name: One\n        run: echo one\n");

        Assert.Throws<InvalidOperationException>(() => WorkflowStep.RunText(path, "Two"));
    }

    [Fact]
    public void RunTextThrowsForAStepWithNoRunCommand()
    {
        string path = Workflow("      - name: One\n        uses: actions/checkout@v7\n");

        Assert.Throws<InvalidOperationException>(() => WorkflowStep.RunText(path, "One"));
    }

    [Fact]
    public async Task RunAsyncGivesTheStepTheLockfileAndTheEnvironmentFile()
    {
        WorkflowStepRun run = await WorkflowStep.RunAsync("cat .github/agents/claude-lock.json >> \"$GITHUB_ENV\"", "locked\n", TestContext.Current.CancellationToken);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("locked\n", run.EnvironmentFile);
    }

    [Fact]
    public async Task RunAsyncReportsTheExitCodeAndTheStandardErrorOfAFailedStep()
    {
        WorkflowStepRun run = await WorkflowStep.RunAsync("echo broken >&2; exit 5", "{}", TestContext.Current.CancellationToken);

        Assert.Equal(5, run.ExitCode);
        Assert.Contains("broken", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncStopsAtTheFirstFailedCommandAsTheRunnersShellDoes()
    {
        WorkflowStepRun run = await WorkflowStep.RunAsync("false; echo after >> \"$GITHUB_ENV\"", "{}", TestContext.Current.CancellationToken);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal("", run.EnvironmentFile);
    }

    [Fact]
    public void TheFactoryWorkflowHasTheLockfileStep()
        => Assert.StartsWith("jq ", WorkflowStep.RunText(
            Path.Combine(StarterChecks.RepoRoot(), ".github", "workflows", "factory.yml"),
            "Read the builder's ids from the lockfile"), StringComparison.Ordinal);
}
