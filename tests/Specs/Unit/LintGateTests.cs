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

    private async Task<LintGate> GateAsync(
        CommandResult format,
        CommandResult status,
        CommandResult? commit = null,
        bool restoreFirst = false,
        CommandResult? restore = null,
        CancellationToken token = default)
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
                    "exec" when spec.Arguments[3] is "restore" => restore ?? new CommandResult(),
                    "exec" => format,
                    "status" => status,
                    "commit" => commit ?? new CommandResult(),
                    _ => new CommandResult(),
                });
            });
        Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "t:1", _directory.FullName, "/tree", token);
        _recorded.Clear();
        _tokens.Clear();
        return new LintGate(sandbox, new Git(mock.Object), restoreFirst: restoreFirst);
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
        LintGate gate = await GateAsync(new CommandResult(), Changed, new CommandResult { ExitCode = 1, StandardError = "no" }, token: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task WithNoRestoreFirstTheGateRunsNoRestore()
    {
        LintGate gate = await GateAsync(new CommandResult(), new CommandResult(), token: TestContext.Current.CancellationToken);

        await gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_recorded, s => s.Command == "docker" && s.Arguments.Contains("restore"));
    }

    [Fact]
    public async Task RestoreFirstRunsALockedRestoreBeforeTheFormat()
    {
        LintGate gate = await GateAsync(new CommandResult(), new CommandResult(), token: TestContext.Current.CancellationToken, restoreFirst: true);

        await gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken);

        List<CommandSpec> docker = [.. _recorded.Where(s => s.Command == "docker")];
        Assert.Equal(2, docker.Count);
        Assert.Equal(["dotnet", "restore", "--locked-mode"], docker[0].Arguments.Skip(2));
        Assert.Equal(["dotnet", "format", "--no-restore"], docker[1].Arguments.Skip(2));
    }

    [Fact]
    public async Task AFailedRestoreThrowsWithTheCodeAndTheOutputAndRunsNoFormatAndNoGit()
    {
        var restore = new CommandResult { ExitCode = 1, StandardOutput = "error NU1004: The package references have changed" };
        LintGate gate = await GateAsync(new CommandResult(), Changed, token: TestContext.Current.CancellationToken, restoreFirst: true, restore: restore);

        LintGateException exception = await Assert.ThrowsAsync<LintGateException>(
            () => gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken));

        Assert.Contains("restore", exception.Message, StringComparison.Ordinal);
        Assert.Contains("error NU1004", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_recorded, s => s.Command == "docker" && s.Arguments.Contains("format"));
        Assert.DoesNotContain(_recorded, s => s.Command == "git");
    }

    [Fact]
    public async Task RestoreFirstLogsTheRestoreLineBeforeTheFormatLine()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { StandardOutput = "abc\n" });
        Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "t:1", _directory.FullName, "/tree", TestContext.Current.CancellationToken);
        using var log = new StringWriter();
        var gate = new LintGate(sandbox, new Git(mock.Object), log, restoreFirst: true);

        await gate.RunAsync("/tree", "m", "n", "e@x", TestContext.Current.CancellationToken);

        string text = log.ToString();
        int restore = text.IndexOf("command: dotnet restore --locked-mode exited 0", StringComparison.Ordinal);
        int format = text.IndexOf("command: dotnet format --no-restore exited 0", StringComparison.Ordinal);
        Assert.True(restore >= 0, text);
        Assert.True(format > restore, text);
    }
}
