using System.Text.Json;
using Core;
using Moq;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

/// <summary>A <see cref="ClaudeCodeBackend"/> alone, as a run drives it through the seam, over a mock runner that answers
/// <c>docker</c> and each <c>claude</c> command with one JSON answer. It starts through <see cref="AgentBackend.Pick"/>, as the
/// program starts it.</summary>
[Binding]
public sealed class ClaudeCodeBackendSteps : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("claude-backend-root-");
    private readonly DirectoryInfo _clone = Directory.CreateTempSubdirectory("claude-backend-clone-");
    private readonly List<CommandSpec> _commands = [];
    private readonly List<string> _progressAtCommand = [];
    private readonly List<(TimeSpan Elapsed, AgentAnswer Answer)> _reported = [];
    private readonly StringWriter _progress = new();
    private readonly StringWriter _log = new();
    private readonly StepClock _clock = new();
    private int _seconds;
    private string _answer = """{"is_error":false,"num_turns":1,"total_cost_usd":0,"result":"done"}""";
    private AgentBackend? _backend;
    private int _started;

    public ClaudeCodeBackendSteps() =>
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

    private AgentBackend Backend => _backend ?? throw new InvalidOperationException("The backend has not started.");

    public void Dispose()
    {
        _progress.Dispose();
        _log.Dispose();
        _root.Delete(recursive: true);
        _clone.Delete(recursive: true);
    }

    [Given("a Claude Code backend over a clone")]
    public static void GivenBackend()
    {
    }

    [Given("claude answers each prompt of the Claude Code backend after {int} seconds with {int} turns and the cost {decimal}")]
    public void GivenAnswer(int seconds, int turns, decimal cost)
    {
        _seconds = seconds;
        _answer = JsonSerializer.Serialize(new { is_error = false, num_turns = turns, total_cost_usd = cost, result = "done" });
    }

    [When("the Claude Code backend starts")]
    public async Task WhenStarts()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        StartBackend start = AgentBackend.Pick(new BackendOptions(
            mock.Object, "factory-sandbox:test", _root.FullName, null, "sbiondolillo", "factory-scratch", "token", "Factory", "factory@example.com",
            Log: _log, Time: _clock, Progress: _progress));
        _backend = await start(
            new BackendStart(new Git(mock.Object), _clone.FullName, "base", "factory/a1b2c3d4-session", "Title", (elapsed, answer) => _reported.Add((elapsed, answer))),
            TestContext.Current.CancellationToken);
        _started = _commands.Count;
    }

    [When("its builder answers a plan prompt")]
    public Task WhenPlan() => Backend.Builder.RunAsync(Request(Phase.Plan), TestContext.Current.CancellationToken);

    [When("its reviewer answers a review prompt")]
    public Task WhenReview() => Backend.Reviewer.RunAsync(Request(Phase.Review), TestContext.Current.CancellationToken);

    [When("the Claude Code backend collects onto the branch {string}")]
    public Task WhenCollects(string branch) => Backend.CollectAsync(branch, TestContext.Current.CancellationToken);

    [When("the Claude Code backend ends")]
    public async Task WhenEnds() => await Backend.DisposeAsync();

    [Then("the progress of the Claude Code backend held the line {string} when docker got the command {string}")]
    public void ThenLineBefore(string line, string command)
    {
        int index = _commands.FindIndex(c => c.Command is "docker" && string.Join(' ', c.Arguments).StartsWith(command, StringComparison.Ordinal));
        Assert.True(index >= 0, $"docker got no command {command}");
        Assert.Contains(line, _progressAtCommand[index].Split(Environment.NewLine));
    }

    [Then("the docker run of the Claude Code backend holds {string} and then {string}, and {string} and then {string}")]
    public void ThenRunHolds(string firstFlag, string firstValue, string secondFlag, string secondValue)
    {
        List<string> arguments = [.. Assert.Single(_commands, c => c.Command is "docker" && c.Arguments[0] is "run").Arguments];
        foreach ((string flag, string value) in new[] { (firstFlag, firstValue), (secondFlag, secondValue) })
        {
            int index = arguments.IndexOf(value);
            Assert.True(index > 0 && arguments[index - 1] == flag, $"docker run: {string.Join(' ', arguments)}");
        }
    }

    [Then("each prompt ran claude through docker exec in the container of the gates")]
    public void ThenClaudeInContainer()
    {
        List<CommandSpec> claude = [.. _commands.Where(c => c.Command is "docker" && c.Arguments.Contains("claude"))];
        Assert.Equal(2, claude.Count);
        Assert.All(claude, c => Assert.Equal(["exec", GateSandbox.ContainerId, "claude"], c.Arguments.Take(3)));
        Assert.Same(Backend.Builder, Backend.Reviewer);
    }

    [Then("the Claude Code backend reported {int} answers, each of {int} turns, the cost {decimal} dollars and {int} seconds")]
    public void ThenReported(int count, int turns, decimal cost, int seconds)
    {
        Assert.Equal(count, _reported.Count);
        Assert.All(_reported, r => Assert.Equal((turns, cost, TimeSpan.FromSeconds(seconds)), (r.Answer.NumTurns, r.Answer.CostUsd, r.Elapsed)));
    }

    [Then("the log of the Claude Code backend holds the line {string}")]
    public void ThenLog(string line) => Assert.Contains(line, _log.ToString().Split(Environment.NewLine));

    [Then("the Claude Code backend ran no command after its start")]
    public void ThenNoCommand() => Assert.Equal(_started, _commands.Count);

    [Then("the last command of the Claude Code backend is {string} of its container")]
    public void ThenLast(string command) =>
        Assert.Equal($"{command} {GateSandbox.ContainerId}", string.Join(' ', ["docker", .. _commands[^1].Arguments]));

    private static AgentRequest Request(Phase phase) =>
        new(FakeAgent.Whole($"The {phase.Text()} prompt."), "claude-sonnet-5-5", 10, phase, []);

    private CommandResult Claude()
    {
        _clock.Advance(_seconds);
        return new CommandResult { StandardOutput = _answer + "\n" };
    }

    private CommandResult Answer(CommandSpec spec)
    {
        _commands.Add(spec);
        _progressAtCommand.Add(_progress.ToString());
        return spec.Arguments[0] switch
        {
            "run" => new CommandResult { StandardOutput = GateSandbox.ContainerId + "\n" },
            "exec" when spec.Arguments.Contains("claude") => Claude(),
            _ => new CommandResult(),
        };
    }
}
