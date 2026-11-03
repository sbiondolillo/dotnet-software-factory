using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A real work tree, a <see cref="FakeAgent"/> and a real Git over a mocked command runner, for the review phase.</summary>
public sealed class ReviewPhaseHarness : IDisposable
{
    public const string DefaultBase = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("factory-review-work-");

    public ReviewPhaseHarness() => Agent = new FakeAgent(_work.FullName);

    public FakeAgent Agent { get; }

    public string WorkTree => _work.FullName;

    public string DiffText { get; set; } = "";

    public int DiffExitCode { get; set; }

    public string? VerdictToWrite { get; set; }

    public bool IsError { get; set; }

    public string ResultText { get; set; } = "";

    public string JobSteps { get; set; } = RecipeRules.ReviewSteps(Recipe.Behavior, RunKind.Factory);

    public List<CommandSpec> Recorded { get; } = [];

    public IReadOnlyList<AgentRequest> Requests => Agent.Requests;

    public IReadOnlyList<string> Prompts => Agent.Prompts;

    public void Dispose() => _work.Delete(recursive: true);

    public void Write(string relative, string text)
    {
        string full = Path.Combine(_work.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public async Task<ReviewPhaseResult> RunAsync(
        string title = "Add a checkout total",
        string body = "The checkout shows the total of the cart.",
        string model = "claude-haiku-4-5-20251001",
        int turns = 30,
        string baseCommit = DefaultBase)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                Recorded.Add(spec);
                return Task.FromResult(Git(spec));
            });
        if (VerdictToWrite is not null)
        {
            Agent.Stage("review.json", VerdictToWrite);
        }

        Agent.Answer(IsError ? FakeAgent.Error(ResultText) : FakeAgent.Done(ResultText));
        var phase = new ReviewPhase(Agent, new Git(mock.Object), JobSteps);
        return await phase.RunAsync(title, body, model, turns, _work.FullName, baseCommit);
    }

    private CommandResult Git(CommandSpec spec)
    {
        _ = spec;
        return new()
        {
            ExitCode = DiffExitCode,
            StandardOutput = DiffText,
            StandardError = DiffExitCode is 0 ? "" : "fatal: bad object",
        };
    }
}
