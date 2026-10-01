using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A mocked command runner behind a real sandbox, answering the build phase's claude call.</summary>
public sealed class BuildPhaseHarness : IDisposable
{
    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("factory-build-work-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-build-root-");

    public bool IsError { get; set; }

    public string ResultText { get; set; } = "";

    public bool OmitResult { get; set; }

    public string? RawOutput { get; set; }

    public List<List<string>> ExecArguments { get; } = [];

    public List<string> Prompts { get; } = [];

    public void Dispose()
    {
        _work.Delete(recursive: true);
        _root.Delete(recursive: true);
    }

    public async Task<BuildPhaseResult> RunAsync(
        string title = "Add a checkout total",
        string body = "The checkout shows the total of the cart.",
        string model = "claude-haiku-4-5-20251001",
        int turns = 60,
        IReadOnlyList<string>? redo = null)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(spec.Arguments[0] switch
            {
                "run" => new CommandResult { StandardOutput = "0123456789abcdef\n" },
                "exec" => Exec(spec),
                _ => new CommandResult(),
            }));
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _root.FullName, _work.FullName);
        return await new BuildPhase(new ClaudeCodeAgent(sandbox)).RunAsync(title, body, model, turns, redo ?? []);
    }

    private CommandResult Exec(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        ExecArguments.Add(arguments);
        Prompts.Add(arguments[arguments.IndexOf("-p") + 1]);
        if (RawOutput is not null)
        {
            return new CommandResult { ExitCode = 1, StandardOutput = RawOutput };
        }

        var json = new Dictionary<string, object> { ["is_error"] = IsError, ["num_turns"] = 1, ["total_cost_usd"] = 0.0 };
        if (!OmitResult)
        {
            json["result"] = ResultText;
        }

        return new CommandResult { StandardOutput = JsonSerializer.Serialize(json) };
    }
}
