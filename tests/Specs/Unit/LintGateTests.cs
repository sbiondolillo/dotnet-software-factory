using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class LintGateTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-lintgate-unit-");
    private readonly List<CommandSpec> _recorded = [];

    public LintGateTests() =>
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

    public void Dispose() => _directory.Delete(recursive: true);

    private async Task<LintGate> GateAsync(int format = 0, string formatError = "", string status = "", string? failing = null)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                if (spec.Command is "git")
                {
                    return Task.FromResult(spec.Arguments[0] == failing
                        ? new CommandResult { ExitCode = 128, StandardError = "fatal: nope" }
                        : new CommandResult { StandardOutput = spec.Arguments[0] is "status" ? status : "" });
                }

                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = "id1\n" },
                    "exec" => new CommandResult { ExitCode = format, StandardError = formatError },
                    _ => new CommandResult(),
                });
            });
        Sandbox sandbox = await Sandbox.StartAsync(
            mock.Object, "t:1", _directory.FullName, "/tree", TestContext.Current.CancellationToken);
        _recorded.Clear();
        return new LintGate(sandbox, new Git(mock.Object));
    }

    private static Task<bool> RunAsync(LintGate gate) =>
        gate.RunAsync("/tree", "msg", "Ada", "ada@example.com", TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1, "boom")]
    [InlineData(2, "")]
    public async Task AFailedFormatThrowsWithCodeAndReasonAndRunsNoGit(int code, string error)
    {
        LintGate gate = await GateAsync(format: code, formatError: error);

        var exception = await Assert.ThrowsAsync<LintGateException>(() => RunAsync(gate));

        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
        Assert.Contains(error, exception.Message);
        Assert.Single(_recorded);
        Assert.Equal(["exec", "id1", "dotnet", "format", "--no-restore"], _recorded[0].Arguments);
    }

    [Fact]
    public async Task AFailedStatusPassesGitCommandExceptionAndRunsNoCommit()
    {
        LintGate gate = await GateAsync(failing: "status");

        var exception = await Assert.ThrowsAsync<GitCommandException>(() => RunAsync(gate));

        Assert.Contains("status", exception.Message);
        Assert.Contains("fatal: nope", exception.Message);
        Assert.DoesNotContain(_recorded, s => s.Arguments[0] is "commit");
    }

    [Fact]
    public async Task AFailedCommitPropagates()
    {
        LintGate gate = await GateAsync(status: " M a.cs\n", failing: "commit");

        await Assert.ThrowsAsync<GitCommandException>(() => RunAsync(gate));
    }

    [Fact]
    public async Task ACleanTreeStopsAfterStatus()
    {
        LintGate gate = await GateAsync(formatError: "warning");

        Assert.False(await RunAsync(gate));
        Assert.Equal(["exec", "status"], _recorded.Select(s => s.Arguments[0]));
    }

    [Fact]
    public async Task AChangedTreeRunsFormatStatusAddCommit()
    {
        LintGate gate = await GateAsync(status: "?? a.cs\n");

        Assert.True(await RunAsync(gate));
        Assert.Equal(["exec", "status", "add", "commit"], _recorded.Select(s => s.Arguments[0]));
    }

    [Fact]
    public async Task TheChangeCheckIsGitStatusPorcelainInTheWorkTree()
    {
        LintGate gate = await GateAsync();

        await RunAsync(gate);

        CommandSpec status = _recorded.Single(s => s.Command is "git");
        Assert.Equal(["status", "--porcelain"], status.Arguments);
        Assert.Equal("/tree", status.WorkingDirectory);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("\n", false)]
    [InlineData(" M a.cs\n?? b.cs\n D c.cs\n", true)]
    public async Task TheChangeCheckReadsTheOutput(string output, bool expected)
    {
        LintGate gate = await GateAsync(status: output);

        Assert.Equal(expected, await RunAsync(gate));
    }

    [Fact]
    public void TheExceptionMessageHoldsCodeAndError() =>
        Assert.Equal("dotnet format failed with exit code 3: bad", new LintGateException(3, "bad").Message);

    [Fact]
    public async Task TheFormatHasNoEnvNames()
    {
        LintGate gate = await GateAsync();

        await RunAsync(gate);

        Assert.DoesNotContain("--env", _recorded[0].Arguments);
    }
}
