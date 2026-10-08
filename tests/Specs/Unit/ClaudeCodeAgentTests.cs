using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class ClaudeCodeAgentTests : IDisposable
{
    private const string Ok = """{"is_error":false,"num_turns":1,"total_cost_usd":0.5,"result":"ok"}""";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-claude-unit-");
    private readonly List<CommandSpec> _recorded = [];

    public void Dispose() => _directory.Delete(recursive: true);

    private async Task<ClaudeCodeAgent> AgentAsync(CommandResult exec)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "id1\n" },
                    "exec" => exec,
                    _ => new CommandResult(),
                });
            });
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Sandbox sandbox = await Sandbox.StartAsync(
            mock.Object, "t:1", _directory.FullName, "/tree", TestContext.Current.CancellationToken);
        return new ClaudeCodeAgent(sandbox);
    }

    private static Task<ClaudeCodeResult> RunAsync(ClaudeCodeAgent agent, string prompt = "hi") =>
        agent.RunAsync(prompt, "m", 1, TestContext.Current.CancellationToken);

    private bool Executed => _recorded.Exists(s => s.Arguments[0] == "exec");

    [Theory]
    [InlineData(1, "not logged in")]
    [InlineData(137, "killed")]
    public async Task NoJsonObjectWithANonzeroExitThrowsWithCodeAndReason(int code, string reason)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { ExitCode = code, StandardError = reason });

        ClaudeCodeException exception = await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent));

        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
        Assert.Contains(reason, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ok\n")]
    [InlineData("[]")]
    public async Task ExitZeroWithNoJsonObjectThrowsClaudeCodeException(string output)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = output });

        await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent));
    }

    [Fact]
    public async Task AnErrorObjectWithExitOneIsAResult()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult
        {
            ExitCode = 1,
            StandardOutput = """{"is_error":true,"num_turns":2,"total_cost_usd":0,"result":"bad"}""",
        });

        ClaudeCodeResult result = await RunAsync(agent);

        Assert.Equal(new ClaudeCodeResult("bad", 2, 0m, true), result);
    }

    [Fact]
    public async Task ANoResultMemberGivesEmptyText()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = """{"is_error":true,"num_turns":2}""" });

        Assert.Equal("", (await RunAsync(agent)).Result);
    }

    [Theory]
    [InlineData(131072, "a")]
    [InlineData(65536, "é")]
    public async Task ATooLongPromptThrowsAndRunsNoExec(int count, string letter)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });

        ClaudeCodeException exception = await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent, string.Concat(Enumerable.Repeat(letter, count))));

        Assert.Contains("131072", exception.Message);
        Assert.False(Executed);
    }

    [Fact]
    public async Task ThePromptOf131071BytesReachesExecWhole()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });
        string prompt = new('a', 131071);

        await RunAsync(agent, prompt);

        List<string> args = [.. _recorded.Single(s => s.Arguments[0] == "exec").Arguments];
        Assert.Equal(prompt, args[args.IndexOf("-p") + 1]);
    }

    [Fact]
    public async Task ExecHoldsNoEnvAndCarriesTheFixedFlags()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });

        await RunAsync(agent);

        List<string> args = [.. _recorded.Single(s => s.Arguments[0] == "exec").Arguments];
        Assert.DoesNotContain("--env", args);
        Assert.Equal(["--output-format", "json", "--dangerously-skip-permissions"], args.TakeLast(3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void ABadFileThrowsWithThePath(string? text)
    {
        string path = Path.Combine(_directory.FullName, "a.json");
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }

        ClaudeCodeOutputException exception = Assert.Throws<ClaudeCodeOutputException>(() => ClaudeCodeAgent.ReadJsonFile(path));

        Assert.Contains(path, exception.Message);
    }
}
