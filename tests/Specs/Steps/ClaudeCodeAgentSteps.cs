using System.Text.Json;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ClaudeCodeAgentSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-claude-steps-");
    private readonly List<CommandSpec> _recorded = [];
    private CommandResult _exec = new();
    private ClaudeCodeAgent? _agent;
    private ClaudeCodeResult? _result;
    private JsonDocument? _document;
    private string _prompt = "";
    private string _path = "";

    public void Dispose()
    {
        _document?.Dispose();
        _directory.Delete(recursive: true);
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
                        "run" => new CommandResult { StandardOutput = "0123456789abcdef\n" },
                        "exec" => _exec,
                        _ => new CommandResult(),
                    });
                });
            File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
            Sandbox sandbox = await Sandbox.StartAsync(
                mock.Object, "factory-sandbox:test", _directory.FullName, "/tmp/factory-work-tree", TestContext.Current.CancellationToken);
            _agent = new ClaudeCodeAgent(sandbox);
        }

        return _agent;
    }

    private List<string> ExecArguments() => [.. _recorded.Single(spec => spec.Arguments[0] == "exec").Arguments];

    private async Task RunAsync(string prompt, string model, int turns)
    {
        ClaudeCodeAgent agent = await AgentAsync();
        try
        {
            _result = await agent.RunAsync(prompt, model, turns, TestContext.Current.CancellationToken);
        }
        catch (ClaudeCodeException exception)
        {
            exceptionState.Exception = exception;
        }
    }

    [Given("a ClaudeCodeAgent built over a Sandbox over a mock CommandRunner")]
    public async Task GivenAnAgent() => await AgentAsync();

    [Given("docker exec exits with the code {int} and writes to standard output {string}")]
    public void GivenExec(int code, string output) => _exec = new CommandResult { ExitCode = code, StandardOutput = output };

    [Given("docker exec exits with the code {int}, writes nothing to standard output and writes a reason to standard error")]
    public void GivenExecFails(int code) => _exec = new CommandResult { ExitCode = code, StandardError = "Not logged in" };

    [Given("a file at a path holds {string}")]
    public void GivenFile(string text)
    {
        _path = Path.Combine(_directory.FullName, "answer.json");
        File.WriteAllText(_path, text);
    }

    [Given("no file is at a path")]
    public void GivenNoFile() => _path = Path.Combine(_directory.FullName, "missing.json");

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

    [When("the caller reads the JSON file at the path")]
    public async Task WhenReads()
    {
        await AgentAsync();
        try
        {
            _document = ClaudeCodeAgent.ReadJsonFile(_path);
        }
        catch (ClaudeCodeOutputException exception)
        {
            exceptionState.Exception = exception;
        }
    }

    [Then("the docker exec arguments hold {string} right after the container id, then {string} and then {string}, {string} and then {string}, and {string} and then {string}")]
    public void ThenArguments(string program, string p, string prompt, string modelFlag, string model, string turnsFlag, string turns)
    {
        List<string> args = ExecArguments();
        Assert.Equal("0123456789abcdef", args[1]);
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

    [Then("the result has the turn count {int}, the cost {double} and an empty result text")]
    public void ThenEmpty(int turns, double cost)
    {
        Assert.Equal(turns, _result!.NumTurns);
        Assert.Equal(cost, (double)_result.CostUsd);
        Assert.Equal("", _result.Result);
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

    [Then("the read throws ClaudeCodeOutputException")]
    public void ThenReadThrows() => Assert.IsType<ClaudeCodeOutputException>(exceptionState.Exception);

    [Then("the exception message holds {string} and the reason")]
    public void ThenMessageReason(string code)
    {
        Assert.Contains(code, exceptionState.Exception!.Message);
        Assert.Contains("Not logged in", exceptionState.Exception.Message);
    }

    [Then("the exception message holds {string}")]
    public void ThenMessage(string text) => Assert.Contains(text, exceptionState.Exception!.Message);

    [Then("the exception message holds the path")]
    public void ThenMessagePath() => Assert.Contains(_path, exceptionState.Exception!.Message);

    [Then("the runner runs no docker exec")]
    public void ThenNoExec() => Assert.DoesNotContain(_recorded, spec => spec.Arguments[0] == "exec");

    [Then("the document root has the property {string} with the value {string} and the property {string} with {int} elements")]
    public void ThenDocument(string name, string value, string items, int count)
    {
        JsonElement root = _document!.RootElement;
        Assert.Equal(value, root.GetProperty(name).GetString());
        Assert.Equal(count, root.GetProperty(items).GetArrayLength());
    }
}
