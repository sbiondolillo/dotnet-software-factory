using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A real work tree and a mocked command runner behind a real sandbox, for the plan phase.</summary>
public sealed class PlanPhaseHarness : IDisposable
{
    public const string SoundFeature =
        "Feature: Checkout\n\n  Scenario: The total is the sum of the prices\n    Given a cart with two items\n    When the customer opens the checkout\n    Then the total is the sum of the two prices\n";

    public const string TwoScenarioFeature = SoundFeature
        + "\n  Scenario: An empty cart has the total zero\n    Given a cart with no item\n    When the customer opens the checkout\n    Then the total is zero\n";

    public const string PlanText = "The checkout adds the prices of the cart.";

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("factory-plan-work-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-plan-root-");
    private readonly List<(int? Run, Action<string> Act)> _actions = [];
    private readonly List<(int? Run, bool IsError, string Text)> _answers = [];
    private int _execs;

    public bool Refactor { get; set; }

    public IssueScenariosResult? Scenarios { get; set; }

    public string? RawOutput { get; set; }

    public string WorkTree => _work.FullName;

    public List<CommandSpec> Recorded { get; } = [];

    public List<string> Prompts { get; } = [];

    public List<List<string>> ExecArguments { get; } = [];

    public void Dispose()
    {
        _work.Delete(recursive: true);
        _root.Delete(recursive: true);
    }

    public void Write(string relative, string text)
    {
        string full = Path.Combine(_work.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Act(int? run, Action<string> action) => _actions.Add((run, action));

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

    public void Answer(int? run, bool isError, string text) => _answers.Add((run, isError, text));

    public async Task<PlanPhaseResult> RunAsync(
        string title = "Add a checkout total",
        string body = "The checkout shows the total of the cart.",
        string model = "claude-haiku-4-5-20251001",
        int turns = 40)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                Recorded.Add(spec);
                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "0123456789abcdef\n" },
                    "exec" => Exec(spec),
                    _ => new CommandResult(),
                });
            });
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _root.FullName, _work.FullName);
        return await new PlanPhase(new ClaudeCodeAgent(sandbox), Refactor, Scenarios).RunAsync(title, body, model, turns, _work.FullName);
    }

    private CommandResult Exec(CommandSpec spec)
    {
        _execs++;
        List<string> arguments = [.. spec.Arguments];
        ExecArguments.Add(arguments);
        Prompts.Add(arguments[arguments.IndexOf("-p") + 1]);
        foreach ((int? run, Action<string> act) in _actions)
        {
            if (run is null || run == _execs)
            {
                act(_work.FullName);
            }
        }

        (int? _, bool isError, string text) = _answers.LastOrDefault(a => a.Run == _execs);
        if (_answers.All(a => a.Run != _execs) && _answers.Any(a => a.Run is null))
        {
            (_, isError, text) = _answers.Last(a => a.Run is null);
        }

        if (RawOutput is not null)
        {
            return new CommandResult { ExitCode = 1, StandardOutput = RawOutput };
        }

        return new CommandResult
        {
            StandardOutput = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["is_error"] = isError,
                ["result"] = text,
                ["num_turns"] = 1,
                ["total_cost_usd"] = 0.0,
            }),
        };
    }
}
