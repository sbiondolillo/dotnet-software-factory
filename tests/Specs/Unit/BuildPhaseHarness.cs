using Core;

namespace Specs.Unit;

/// <summary>A <see cref="FakeAgent"/> that answers the build phase's prompt.</summary>
public sealed class BuildPhaseHarness : IDisposable
{
    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("factory-build-work-");

    public BuildPhaseHarness() => Agent = new FakeAgent(_work.FullName);

    public bool IsError { get; set; }

    public string ResultText { get; set; } = "";

    /// <summary>The answer of the agent, in place of the one that <see cref="IsError"/> and <see cref="ResultText"/> make.</summary>
    public AgentAnswer? Answer { get; set; }

    public BuildText Text { get; set; } = RecipeRules.BuildText(Recipe.Behavior, RunKind.Factory);

    public FakeAgent Agent { get; }

    public IReadOnlyList<AgentRequest> Requests => Agent.Requests;

    public IReadOnlyList<string> Prompts => Agent.Prompts;

    public void Dispose() => _work.Delete(recursive: true);

    public async Task<BuildPhaseResult> RunAsync(
        string title = "Add a checkout total",
        string body = "The checkout shows the total of the cart.",
        string model = "claude-haiku-4-5-20251001",
        int turns = 60,
        IReadOnlyList<string>? redo = null,
        IReadOnlyList<string>? outputs = null)
    {
        Agent.Answer(Answer ?? (IsError ? FakeAgent.Error(ResultText) : FakeAgent.Done(ResultText)));
        return await new BuildPhase(Agent, Text).RunAsync(title, body, model, turns, redo ?? [], outputs ?? []);
    }
}
