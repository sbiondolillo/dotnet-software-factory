using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class RunLogTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-runlog-unit-");
    private readonly List<CommandSpec> _recorded = [];
    private readonly StringWriter _log = new();

    public RunLogTests() =>
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

    public void Dispose() => _directory.Delete(recursive: true);

    private string[] Lines => [.. ConsoleHarness.Lines(_log)];

    private async Task<Sandbox> SandboxAsync(Func<CommandSpec, CommandResult> exec)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "id1\n" },
                    "exec" => exec(spec),
                    _ => new CommandResult(),
                });
            });
        return await Sandbox.StartAsync(mock.Object, "t:1", _directory.FullName, "/tree", TestContext.Current.CancellationToken);
    }

    private async Task<ClaudeCodeResult> AskAsync(CommandResult answer, string prompt = "hi")
    {
        Sandbox sandbox = await SandboxAsync(_ => answer);
        return await new ClaudeCodeAgent(sandbox, _log, new FakeTime()).RunAsync(prompt, "m", 1, "plan", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NoJsonObjectWritesTheStartLineAndNoEndLine()
    {
        await Assert.ThrowsAsync<ClaudeCodeException>(() => AskAsync(new CommandResult { ExitCode = 1, StandardError = "x" }));
        Assert.Equal(["agent: plan start"], Lines);
    }

    [Fact]
    public async Task AnErrorAnswerStillWritesTheEndLine()
    {
        await AskAsync(new CommandResult { StandardOutput = """{"is_error":true,"num_turns":3,"total_cost_usd":0.2500}""" });
        Assert.Equal(["agent: plan start", "agent: plan end, 3 turns, $0.2500, 0 s"], Lines);
    }

    [Fact]
    public async Task AnAnswerWithNoTurnsAndNoCostWritesZeros()
    {
        await AskAsync(new CommandResult { StandardOutput = """{"is_error":false}""" });
        Assert.Equal("agent: plan end, 0 turns, $0, 0 s", Lines[1]);
    }

    [Fact]
    public async Task APromptOf131072BytesWritesNoLineAndRunsNoCommand()
    {
        await Assert.ThrowsAsync<ClaudeCodeException>(() => AskAsync(new CommandResult(), new string('a', 131072)));
        Assert.Empty(Lines);
        Assert.DoesNotContain(_recorded, s => s.Arguments[0] is "exec");
    }

    [Fact]
    public async Task AFailedFormatWritesItsLineBeforeTheThrow()
    {
        Sandbox sandbox = await SandboxAsync(_ => new CommandResult { ExitCode = 2 });
        var gate = new LintGate(sandbox, new Git(Mock.Of<CommandRunner>(MockBehavior.Strict)), _log);
        await Assert.ThrowsAsync<LintGateException>(() => gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken));
        Assert.Equal(["command: dotnet format --no-restore exited 2"], Lines);
    }

    [Fact]
    public async Task APinFaultRunsNoDotnetCommandAndWritesNoLine()
    {
        using var harness = new TestGateHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(harness.WorkTree.FullName, "global.json"), "{}");
        TestGateResult result = await new TestGate(harness.Sandbox!, _log).RunAsync(
            harness.WorkTree.FullName, harness.Pin, [], TestContext.Current.CancellationToken);
        Assert.False(result.Passed);
        Assert.Empty(harness.Seen);
        Assert.Empty(Lines);
    }

    [Fact]
    public async Task AFailedTestCommandWritesItsLine()
    {
        using var harness = new TestGateHarness();
        harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        harness.Trx = null;
        await harness.StartAsync(TestContext.Current.CancellationToken);
        TestGateResult result = await new TestGate(harness.Sandbox!, _log).RunAsync(
            harness.WorkTree.FullName, harness.Pin, [], TestContext.Current.CancellationToken);
        Assert.False(result.Passed);
        string last = Lines[^1];
        Assert.StartsWith("command: dotnet test --no-restore --logger trx --results-directory /work/", last, StringComparison.Ordinal);
        Assert.EndsWith(" exited 1", last, StringComparison.Ordinal);
        Assert.Equal(4, Lines.Length);
    }
}
