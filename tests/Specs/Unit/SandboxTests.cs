using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class SandboxTests : IDisposable
{
    private const string Id = "abc123";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-sandbox-unit-");
    private readonly List<CommandSpec> _recorded = [];
    private readonly List<CancellationToken> _tokens = [];

    public SandboxTests() =>
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

    public void Dispose() => _directory.Delete(recursive: true);

    private CommandRunner Runner(int inspect = 0, int build = 0, int run = 0, string runError = "", int exec = 0)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken token) =>
            {
                _recorded.Add(spec);
                _tokens.Add(token);
                return Task.FromResult(spec.Arguments[0] switch
                {
                    "image" => new CommandResult { ExitCode = inspect },
                    "build" => new CommandResult { ExitCode = build, StandardError = "build broke" },
                    "run" => new CommandResult { ExitCode = run, StandardOutput = Id + "\n", StandardError = runError },
                    "exec" => new CommandResult { ExitCode = exec },
                    _ => new CommandResult(),
                });
            });
        return mock.Object;
    }

    private Task<Sandbox> StartAsync(CommandRunner runner, CancellationToken token = default) =>
        Sandbox.StartAsync(runner, "t:1", _directory.FullName, "/tree", token);

    [Theory]
    [InlineData(1, "boom")]
    [InlineData(125, "")]
    public async Task AFailedRunThrowsWithTheStandardErrorAndStartsNothingLater(int code, string error)
    {
        SandboxException exception = await Assert.ThrowsAsync<SandboxException>(
            () => StartAsync(Runner(run: code, runError: error), TestContext.Current.CancellationToken));

        Assert.Contains(error, exception.Message);
        Assert.Equal(["image", "run"], _recorded.Select(s => s.Arguments[0]));
    }

    [Fact]
    public async Task AFailedBuildThrowsAndRunsNoDockerRun()
    {
        await Assert.ThrowsAsync<SandboxImageException>(
            () => StartAsync(Runner(inspect: 1, build: 2), TestContext.Current.CancellationToken));

        Assert.DoesNotContain(_recorded, s => s.Arguments[0] == "run");
    }

    [Fact]
    public async Task APresentImageRunsNoBuildButRuns()
    {
        await StartAsync(Runner(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_recorded, s => s.Arguments[0] == "build");
        Assert.Contains(_recorded, s => s.Arguments[0] == "run");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(127)]
    public async Task ANonzeroExecExitIsAResult(int code)
    {
        await using Sandbox sandbox = await StartAsync(Runner(exec: code), TestContext.Current.CancellationToken);

        CommandResult result = await sandbox.RunAsync("x", [], [], TestContext.Current.CancellationToken);

        Assert.Equal(code, result.ExitCode);
    }

    [Fact]
    public async Task TheIdIsTrimmedInExecAndRmAndNamesAppearAloneWithNoSecretInRun()
    {
        Runner();
        CommandRunner runner = Runner();
        await using (Sandbox sandbox = await StartAsync(runner, TestContext.Current.CancellationToken))
        {
            await sandbox.RunAsync("p", ["a"], ["N1"], TestContext.Current.CancellationToken);
        }

        CommandSpec exec = _recorded.Single(s => s.Arguments[0] == "exec");
        Assert.Equal(["exec", "--env", "N1", Id, "p", "a"], exec.Arguments);
        Assert.Empty(exec.EnvironmentVariables);
        Assert.Equal(["rm", "--force", Id], _recorded[^1].Arguments);
        CommandSpec run = _recorded.Single(s => s.Arguments[0] == "run");
        Assert.DoesNotContain(run.Arguments, a => a.StartsWith("ANTHROPIC_API_KEY=", StringComparison.Ordinal));
        Assert.Empty(run.EnvironmentVariables);
    }

    [Fact]
    public async Task ACancelledTokenReachesStartAndExec()
    {
        using var cts = new CancellationTokenSource();
        await using Sandbox sandbox = await StartAsync(Runner(), cts.Token);
        await sandbox.RunAsync("p", [], [], cts.Token);

        Assert.All(_tokens.Take(_tokens.Count), t => Assert.Equal(cts.Token, t));
    }
}
