using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A real work tree and a mocked command runner behind a real sandbox and a real Git, for the review phase.</summary>
public sealed class ReviewPhaseHarness : IDisposable
{
    public const string DefaultBase = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("factory-review-work-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-review-root-");

    public string WorkTree => _work.FullName;

    public string DiffText { get; set; } = "";

    public int DiffExitCode { get; set; }

    public string? VerdictToWrite { get; set; }

    public bool IsError { get; set; }

    public string ResultText { get; set; } = "";

    public List<CommandSpec> Recorded { get; } = [];

    public List<List<string>> ExecArguments { get; } = [];

    public List<string> Prompts { get; } = [];

    public string? VerdictBeforeAgent { get; private set; }

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
                return Task.FromResult(spec.Command is "git" ? Git(spec) : spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "0123456789abcdef\n" },
                    "exec" => Exec(spec),
                    _ => new CommandResult(),
                });
            });
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _root.FullName, _work.FullName);
        var phase = new ReviewPhase(new ClaudeCodeAgent(sandbox), new Git(mock.Object));
        return await phase.RunAsync(title, body, model, turns, _work.FullName, baseCommit);
    }

    private CommandResult Git(CommandSpec spec) => new()
    {
        ExitCode = DiffExitCode,
        StandardOutput = DiffText,
        StandardError = DiffExitCode is 0 ? "" : "fatal: bad object",
    };

    private CommandResult Exec(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        ExecArguments.Add(arguments);
        Prompts.Add(arguments[arguments.IndexOf("-p") + 1]);
        string verdict = Path.Combine(_work.FullName, ".factory", "review.json");
        VerdictBeforeAgent = File.Exists(verdict) ? File.ReadAllText(verdict) : null;
        if (VerdictToWrite is not null)
        {
            Write(".factory/review.json", VerdictToWrite);
        }

        return new CommandResult
        {
            StandardOutput = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["is_error"] = IsError,
                ["result"] = ResultText,
                ["num_turns"] = 1,
                ["total_cost_usd"] = 0.0,
            }),
        };
    }
}
