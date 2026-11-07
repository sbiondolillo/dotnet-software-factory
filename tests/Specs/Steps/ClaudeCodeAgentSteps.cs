using Core;
using Moq;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ClaudeCodeAgentSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-claude-steps-");
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-claude-steps-tree-");
    private readonly List<CommandSpec> _recorded = [];
    private readonly StringWriter _log = new();
    private readonly FakeTime _time = new();
    private readonly List<(TimeSpan Elapsed, AgentAnswer Answer)> _reported = [];
    private string? _logAtExec;
    private double _execSeconds;
    private CommandResult _exec = new();
    private (string Path, string Text)? _write;
    private ClaudeCodeAgent? _agent;
    private AgentAnswer? _result;
    private string _prompt = "";

    public void Dispose()
    {
        _log.Dispose();
        _directory.Delete(recursive: true);
        _tree.Delete(recursive: true);
    }

    private void WriteTree(string relative, string text)
    {
        string full = Path.Combine(_tree.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private CommandResult Exec()
    {
        _logAtExec = _log.ToString();
        _time.WaitAsync(TimeSpan.FromSeconds(_execSeconds), CancellationToken.None).GetAwaiter().GetResult();
        if (_write is (string path, string text))
        {
            WriteTree(path, text);
        }

        return _exec;
    }

    private async Task<ClaudeCodeAgent> AgentAsync()
    {
        if (_agent is null)
        {
            var mock = new Mock<CommandRunner>(MockBehavior.Strict);
            mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
                .Returns((CommandSpec spec, CancellationToken _) =>
                {
                    _recorded.Add(spec);
                    return Task.FromResult(spec.Arguments[0] switch
                    {
                        "run" => new CommandResult { StandardOutput = GateSandbox.ContainerId + "\n" },
                        "exec" => Exec(),
                        _ => new CommandResult(),
                    });
                });
            Sandbox sandbox = await Sandbox.StartAsync(
                mock.Object, "factory-sandbox:test", _directory.FullName, _tree.FullName, TestContext.Current.CancellationToken);
            _agent = new ClaudeCodeAgent(sandbox, _tree.FullName, _log, _time, (elapsed, answer) => _reported.Add((elapsed, answer)));
        }

        return _agent;
    }

    private List<string> ExecArguments() => [.. _recorded.Single(spec => spec.Arguments[0] == "exec").Arguments];

    private async Task RunAsync(string prompt, string model, int turns, params string[] outputs) =>
        await RunAsync(prompt, model, turns, Phase.Build, outputs);

    private async Task RunAsync(string prompt, string model, int turns, Phase phase, params string[] outputs)
    {
        ClaudeCodeAgent agent = await AgentAsync();
        try
        {
            _result = await agent.RunAsync(new AgentRequest(FakeAgent.Whole(prompt), model, turns, phase, outputs), TestContext.Current.CancellationToken);
        }
        catch (ClaudeCodeException exception)
        {
            exceptionState.Exception = exception;
        }
    }

    [Given("a ClaudeCodeAgent over a Sandbox")]
    public async Task GivenAnAgent() => await AgentAsync();

    [Given("docker exec exits with the code {int} and writes to standard output {string}")]
    public void GivenExec(int code, string output) => _exec = new CommandResult { ExitCode = code, StandardOutput = output };

    [Given("docker exec exits with the code {int}, writes nothing to standard output and writes a reason to standard error")]
    public void GivenExecFails(int code) => _exec = new CommandResult { ExitCode = code, StandardError = "Not logged in" };

    [Given("docker exec writes {string} to {string}, exits with the code {int} and writes to standard output {string}")]
    public void GivenExecWrites(string text, string path, int code, string output)
    {
        _write = (path, text);
        _exec = new CommandResult { ExitCode = code, StandardOutput = output };
    }

    [Given("docker exec takes {double} seconds and writes to standard output {string}")]
    public void GivenExecTakes(double seconds, string output)
    {
        _execSeconds = seconds;
        _exec = new CommandResult { StandardOutput = output };
    }

    [When("a caller runs a {word} prompt")]
    public Task WhenRunsPhase(string phase) =>
        RunAsync($"The {phase} prompt.", "claude-sonnet-5-5", 10, Enum.Parse<Phase>(phase, ignoreCase: true));

    [Then("the log held the line {string} when docker exec started")]
    public void ThenLogBeforeExec(string line)
    {
        Assert.NotNull(_logAtExec);
        Assert.Contains(line, _logAtExec.Split(Environment.NewLine));
    }

    [Then("the log holds the line {string}")]
    public void ThenLogHolds(string line) => Assert.Contains(line, _log.ToString().Split(Environment.NewLine));

    [Then("the caller got one answer of {int} turns and the cost {decimal} after {double} seconds")]
    public void ThenReported(int turns, decimal cost, double seconds)
    {
        (TimeSpan elapsed, AgentAnswer answer) = Assert.Single(_reported);
        Assert.Equal(TimeSpan.FromSeconds(seconds), elapsed);
        Assert.Equal(turns, answer.NumTurns);
        Assert.Equal(cost, answer.CostUsd);
    }

    [Given("the work tree file {string} holds {string} before the prompt")]
    public void GivenTreeFile(string path, string text) => WriteTree(path, text);

    [When("a caller runs the prompt {string} with the model {string} and the turn limit {int}")]
    public async Task WhenRuns(string prompt, string model, int turns)
    {
        _prompt = prompt;
        await RunAsync(prompt, model, turns);
    }

    [When("a caller runs a prompt of {int} characters, each one the letter {string}, with the model {string} and the turn limit {int}")]
    public async Task WhenRunsLong(int count, string letter, string model, int turns)
    {
        _prompt = string.Concat(Enumerable.Repeat(letter, count));
        await RunAsync(_prompt, model, turns);
    }

    [When("a caller runs the prompt {string} with the model {string}, the turn limit {int} and the output {string}")]
    public async Task WhenRunsWithOutput(string prompt, string model, int turns, string output)
    {
        _prompt = prompt;
        await RunAsync(prompt, model, turns, output);
    }

    [Then("the docker exec arguments hold {string} right after the container id, then {string} and then {string}, {string} and then {string}, and {string} and then {string}")]
    public void ThenArguments(string program, string p, string prompt, string modelFlag, string model, string turnsFlag, string turns)
    {
        List<string> args = ExecArguments();
        Assert.Equal(GateSandbox.ContainerId, args[1]);
        Assert.Equal([program, p, prompt, modelFlag, model, turnsFlag, turns], args.Skip(2).Take(7));
    }

    [Then("the docker exec arguments hold {string} and then that prompt")]
    public void ThenPrompt(string flag)
    {
        List<string> args = ExecArguments();
        Assert.Equal(_prompt, args[args.IndexOf(flag) + 1]);
    }

    [Then("the result error flag is {word}")]
    public void ThenFlag(string flag) => Assert.Equal(flag == "true", _result!.IsError);

    [Then("the result has the turn count {int}, the cost {double} and the result text {string}")]
    public void ThenValues(int turns, double cost, string text)
    {
        Assert.Equal(turns, _result!.NumTurns);
        Assert.Equal(cost, (double)_result.CostUsd);
        Assert.Equal(text, _result.Result);
    }

    [Then("the result has the turn count {int} and the cost {int}")]
    public void ThenCost(int turns, int cost)
    {
        Assert.Equal(turns, _result!.NumTurns);
        Assert.Equal(cost, _result.CostUsd);
    }

    [Then("the result text is the sentence the JSON object holds")]
    public void ThenSentence() => Assert.Equal("Invalid API key", _result!.Result);

    [Then("the run throws ClaudeCodeException")]
    public void ThenThrows() => Assert.IsType<ClaudeCodeException>(exceptionState.Exception);

    [Then("the exception message holds {string} and the reason")]
    public void ThenMessageReason(string code)
    {
        Assert.Contains(code, exceptionState.Exception!.Message, StringComparison.Ordinal);
        Assert.Contains("Not logged in", exceptionState.Exception.Message, StringComparison.Ordinal);
    }

    [Then("the exception message holds {string}")]
    public void ThenMessage(string text) => Assert.Contains(text, exceptionState.Exception!.Message, StringComparison.Ordinal);

    [Then("ClaudeCodeAgent ran no docker exec")]
    public void ThenNoExec() => Assert.DoesNotContain(_recorded, spec => spec.Arguments[0] == "exec");

    [Then("the result holds the output {string} with the text {string}")]
    public void ThenOutput(string name, string text) => Assert.Equal(new Dictionary<string, string> { [name] = text }, _result!.Outputs);

    [Then("the result holds no output")]
    public void ThenNoOutput() => Assert.Empty(_result!.Outputs);

    [Then("the work tree of the prompt has no file {string}")]
    public void ThenNoFile(string path) => Assert.False(File.Exists(Path.Combine(_tree.FullName, path)));
}
