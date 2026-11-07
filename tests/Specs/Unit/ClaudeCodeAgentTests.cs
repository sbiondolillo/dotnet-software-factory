using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class ClaudeCodeAgentTests : IDisposable
{
    private const string Ok = """{"is_error":false,"num_turns":1,"total_cost_usd":0.5,"result":"ok"}""";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-claude-unit-");
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-claude-tree-");
    private readonly List<CommandSpec> _recorded = [];

    public void Dispose()
    {
        _directory.Delete(recursive: true);
        _tree.Delete(recursive: true);
    }

    private string Output(string name) => Path.Combine(_tree.FullName, ".factory", name);

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(Path.Combine(_tree.FullName, ".factory"));
        File.WriteAllText(Output(name), text);
    }

    private Task<ClaudeCodeAgent> AgentAsync(CommandResult exec) => AgentAsync(() => exec);

    private async Task<ClaudeCodeAgent> AgentAsync(Func<CommandResult> exec)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "id1\n" },
                    "exec" => exec(),
                    _ => new CommandResult(),
                });
            });
        Sandbox sandbox = await Sandbox.StartAsync(
            mock.Object, "t:1", _directory.FullName, _tree.FullName, TestContext.Current.CancellationToken);
        return new ClaudeCodeAgent(sandbox, _tree.FullName);
    }

    private static Task<AgentAnswer> RunAsync(ClaudeCodeAgent agent, string prompt = "hi", params string[] outputs) =>
        agent.RunAsync(new AgentRequest(FakeAgent.Whole(prompt), "m", 1, Phase.Build, outputs), TestContext.Current.CancellationToken);

    private bool Executed => _recorded.Exists(s => s.Arguments[0] == "exec");

    [Theory]
    [InlineData(1, "not logged in")]
    [InlineData(137, "killed")]
    public async Task NoJsonObjectWithANonzeroExitThrowsWithCodeAndReason(int code, string reason)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { ExitCode = code, StandardError = reason });

        ClaudeCodeException exception = await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent));

        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
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

        AgentAnswer result = await RunAsync(agent);

        Assert.Equal(("bad", 2, 0m, true), (result.Result, result.NumTurns, result.CostUsd, result.IsError));
        Assert.Empty(result.Outputs);
        Assert.Empty(result.Faults);
    }

    [Theory]
    [InlineData("""{"is_error":true,"num_turns":2}""")]
    [InlineData("""{"is_error":true,"num_turns":2,"result":" \n"}""")]
    public async Task AnErrorWithNoTextGivesNoTextError(string output)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { ExitCode = 1, StandardOutput = output });

        AgentAnswer answer = await RunAsync(agent);

        Assert.True(answer.IsError);
        Assert.Equal(Agent.NoTextError, answer.Result);
    }

    [Fact]
    public async Task AnAnswerWithNoErrorAndNoTextKeepsTheEmptyText()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = """{"is_error":false,"num_turns":2}""" });

        Assert.Equal("", (await RunAsync(agent)).Result);
    }

    [Fact]
    public async Task ANamedOutputThatThePromptWritesComesBackAsTextAndOneThatItDoesNotIsAbsent()
    {
        Write("plan.md", "the plan of the turn before");
        Write("review.json", "the verdict of the turn before");
        bool[] seenBefore = [];
        ClaudeCodeAgent agent = await AgentAsync(() =>
        {
            seenBefore = [File.Exists(Output("plan.md")), File.Exists(Output("review.json"))];
            Write("plan.md", "the new plan");
            Write("other.md", "not asked for");
            return new CommandResult { StandardOutput = Ok };
        });

        AgentAnswer answer = await RunAsync(agent, "hi", "plan.md", "review.json");

        Assert.Equal([false, false], seenBefore);
        Assert.False(answer.IsError);
        Assert.Equal(new Dictionary<string, string> { ["plan.md"] = "the new plan" }, answer.Outputs);
        Assert.False(File.Exists(Output("review.json")));
        Assert.Equal("the new plan", File.ReadAllText(Output("plan.md")));
    }

    [Fact]
    public async Task AFileThatTheRequestDoesNotNameStaysAndIsNotInTheAnswer()
    {
        Write("issue.md", "the issue");
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });

        AgentAnswer answer = await RunAsync(agent, "hi", "plan.md");

        Assert.Empty(answer.Outputs);
        Assert.Equal("the issue", File.ReadAllText(Output("issue.md")));
    }

    [Theory]
    [InlineData(131072, "a")]
    [InlineData(65536, "é")]
    public async Task ATooLongPromptThrowsAndRunsNoExec(int count, string letter)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });

        ClaudeCodeException exception = await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent, string.Concat(Enumerable.Repeat(letter, count))));

        Assert.Equal("The issue and the job hold more than 131071 bytes in UTF-8, and no Redo text can be cut to make room.", exception.Message);
        Assert.False(Executed);
    }

    [Fact]
    public async Task APromptWhoseFixedPartIsOverTheLimitThrowsAndRunsNoExec()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });
        var prompt = new AgentPrompt(["piece"], pieces => new string('b', 131071) + pieces[0]);

        await Assert.ThrowsAsync<ClaudeCodeException>(() =>
            agent.RunAsync(new AgentRequest(prompt, "m", 1, Phase.Build, []), TestContext.Current.CancellationToken));

        Assert.False(Executed);
    }

    [Fact]
    public async Task APieceOverTheLimitReachesExecCutToFit()
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });
        var prompt = new AgentPrompt([new string('a', 200000)], pieces => "Lead.\n" + pieces[0]);

        await agent.RunAsync(new AgentRequest(prompt, "m", 1, Phase.Build, []), TestContext.Current.CancellationToken);

        List<string> args = [.. _recorded.Single(s => s.Arguments[0] == "exec").Arguments];
        Assert.Equal(prompt.Fit(131071), args[args.IndexOf("-p") + 1]);
        Assert.Equal(131071, System.Text.Encoding.UTF8.GetByteCount(args[args.IndexOf("-p") + 1]));
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
    [InlineData("x; rm -rf /")]
    [InlineData("../secret")]
    [InlineData("/etc/passwd")]
    [InlineData("..")]
    public async Task AnOutputNameThatIsNotAPlainBaseNameThrowsAndRunsNoExec(string name)
    {
        ClaudeCodeAgent agent = await AgentAsync(new CommandResult { StandardOutput = Ok });

        await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(agent, "hi", name));

        Assert.False(Executed);
    }

    [Fact]
    public async Task AnOutputThatThePromptMakesALinkThrowsAndHoldsNoTextOfItsTarget()
    {
        string outside = Path.Combine(_directory.FullName, "secret.txt");
        File.WriteAllText(outside, "a secret of the runner");
        ClaudeCodeAgent agent = await AgentAsync(() =>
        {
            Directory.CreateDirectory(Path.Combine(_tree.FullName, ".factory"));
            File.CreateSymbolicLink(Output("review.json"), outside);
            return new CommandResult { StandardOutput = Ok };
        });

        ClaudeCodeException exception = await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent, "hi", "review.json"));

        Assert.Contains("symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFactoryDirectoryThatThePromptMakesALinkThrows()
    {
        DirectoryInfo outside = Directory.CreateDirectory(Path.Combine(_directory.FullName, "elsewhere"));
        File.WriteAllText(Path.Combine(outside.FullName, "plan.md"), "a file of the runner");
        ClaudeCodeAgent agent = await AgentAsync(() =>
        {
            Directory.CreateSymbolicLink(Path.Combine(_tree.FullName, ".factory"), outside.FullName);
            return new CommandResult { StandardOutput = Ok };
        });

        await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent, "hi", "plan.md"));
    }

    [Fact]
    public async Task AFactoryDirectoryThatIsALinkBeforeThePromptThrowsAndDeletesNothingOutside()
    {
        DirectoryInfo outside = Directory.CreateDirectory(Path.Combine(_directory.FullName, "elsewhere"));
        string kept = Path.Combine(outside.FullName, "plan.md");
        File.WriteAllText(kept, "a file of the runner");
        Directory.CreateSymbolicLink(Path.Combine(_tree.FullName, ".factory"), outside.FullName);
        bool ran = false;
        ClaudeCodeAgent agent = await AgentAsync(() =>
        {
            ran = true;
            return new CommandResult { StandardOutput = Ok };
        });

        await Assert.ThrowsAsync<ClaudeCodeException>(() => RunAsync(agent, "hi", "plan.md"));

        Assert.False(ran);
        Assert.Equal("a file of the runner", File.ReadAllText(kept));
    }
}
