using Core;

namespace Specs.Unit;

/// <summary>A real work tree and a <see cref="FakeAgent"/>, for the plan phase.</summary>
public sealed class PlanPhaseHarness : IDisposable
{
    public const string SoundFeature =
        "Feature: Checkout\n\n  Scenario: The total is the sum of the prices\n    Given a cart with two items\n    When the customer opens the checkout\n    Then the total is the sum of the two prices\n";

    public const string TwoScenarioFeature = SoundFeature
        + "\n  Scenario: An empty cart has the total zero\n    Given a cart with no item\n    When the customer opens the checkout\n    Then the total is zero\n";

    public const string PlanText = "The checkout adds the prices of the cart.";

    private readonly DirectoryInfo _work;

    public PlanPhaseHarness()
    {
        _work = Directory.CreateTempSubdirectory("factory-plan-work-");
        Agent = new FakeAgent(_work.FullName);
    }

    public PlanText Text { get; set; } = RecipeRules.PlanText(Recipe.Behavior, new PlanStep("Plan the work of the issue below.", PlanInput.IssueScenarios));

    public string WorkTree => _work.FullName;

    public FakeAgent Agent { get; }

    public IReadOnlyList<AgentRequest> Requests => Agent.Requests;

    public IReadOnlyList<string> Prompts => Agent.Prompts;

    public void Dispose() => _work.Delete(recursive: true);

    public void Write(string relative, string text)
    {
        string full = Path.Combine(_work.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Act(int? run, Action<string> action) => Agent.Act(action, run);

    public void AgentWrites(int? run, string relative, string text) => Act(run, root =>
    {
        string full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    });

    public void AgentRemoves(int? run, string relative) => Act(run, root => File.Delete(Path.Combine(root, relative)));

    public void AgentChangesOneByte(int? run, string relative) => Act(run, root =>
    {
        string full = Path.Combine(root, relative);
        byte[] bytes = File.ReadAllBytes(full);
        bytes[0] = bytes[0] is (byte)'Z' ? (byte)'Y' : (byte)'Z';
        File.WriteAllBytes(full, bytes);
    });

    public void AgentWritesSoundFiles(int? run)
    {
        AgentWrites(run, "specs/features/checkout.feature", SoundFeature);
        AgentWrites(run, ".factory/plan.md", PlanText);
    }

    public void Answer(int? run, bool isError, string text) => Agent.Answer(isError ? FakeAgent.Error(text) : FakeAgent.Done(text), run);

    public async Task<PlanPhaseResult> RunAsync(
        string title = "Add a checkout total",
        string body = "The checkout shows the total of the cart.",
        string model = "claude-haiku-4-5-20251001",
        int turns = 40) =>
        await new PlanPhase(Agent, Text).RunAsync(title, body, model, turns, new FileHashes(_work.FullName), new FeatureFiles(_work.FullName));
}
