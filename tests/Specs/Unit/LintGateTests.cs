using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class LintGateTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-lintgate-unit-");
    private readonly List<CommandSpec> _recorded = [];
    private readonly List<CancellationToken> _tokens = [];

    public LintGateTests() =>
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

    public void Dispose() => _directory.Delete(recursive: true);

    private async Task<LintGate> GateAsync(CommandResult format, CommandResult status, CommandResult? commit = null, CancellationToken token = default)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken t) =>
            {
                _recorded.Add(spec);
                _tokens.Add(t);
                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "abc\n" },
                    "exec" => format,
                    "status" => status,
                    "commit" => commit ?? new CommandResult(),
                    _ => new CommandResult(),
                });
            });
        Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "t:1", _directory.FullName, "/tree", token);
        _recorded.Clear();
        _tokens.Clear();
        return new LintGate(sandbox, new Git(mock.Object));
    }

    private static CommandResult Changed => new() { StandardOutput = " M A.cs\n" };

    [Theory]
    [InlineData(1, "Could not find a project")]
    [InlineData(2, "")]
    public async Task AFailedFormatThrowsWithTheCodeAndReasonAndRunsNoGit(int code, string error)
    {
        LintGate gate = await GateAsync(new CommandResult { ExitCode = code, StandardError = error }, Changed, token: TestContext.Current.CancellationToken);

        LintGateException exception = await Assert.ThrowsAsync<LintGateException>(
            () => gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken));

        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains(error, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_recorded, s => s.Command == "git");
    }

    [Fact]
    public async Task StandardErrorWithExitCodeZeroAndNoChangeMakesNoCommit()
    {
        LintGate gate = await GateAsync(new CommandResult { StandardError = "An issue was encountered verifying workloads" }, new CommandResult(), token: TestContext.Current.CancellationToken);

        Assert.False(await gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_recorded, s => s.Command == "git" && s.Arguments[0] == "commit");
    }

    [Fact]
    public async Task AFailedChangeQueryThrowsAndMakesNoCommit()
    {
        LintGate gate = await GateAsync(new CommandResult(), new CommandResult { ExitCode = 128, StandardError = "fatal" }, token: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<GitCommandException>(() => gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_recorded, s => s.Command == "git" && s.Arguments[0] is "add" or "commit");
    }

    [Fact]
    public async Task AFailedCommitPropagates()
    {
        LintGate gate = await GateAsync(new CommandResult(), Changed, new CommandResult { ExitCode = 1, StandardError = "no" }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<GitCommandException>(() => gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheTokenReachesEveryCallAndFormatHasNoEnvName()
    {
        using var cts = new CancellationTokenSource();
        LintGate gate = await GateAsync(new CommandResult(), Changed, token: TestContext.Current.CancellationToken);

        Assert.True(await gate.RunAsync("/tree", "m", "n", "e@x", cts.Token));

        Assert.All(_tokens, t => Assert.Equal(cts.Token, t));
        Assert.DoesNotContain("--env", _recorded[0].Arguments);
        Assert.Equal("/tree", _recorded[1].WorkingDirectory);
    }
}
