using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class SandboxImageTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sandbox-unit-").FullName;
    private readonly List<CommandSpec> _recorded = [];
    private readonly List<CancellationToken> _tokens = [];

    public void Dispose() => Directory.Delete(_directory, true);

    private SandboxImage Image(int inspect, int build = 0, string error = "")
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(m => m.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken token) =>
            {
                _recorded.Add(spec);
                _tokens.Add(token);
                return Task.FromResult(spec.Arguments[0] is "image"
                    ? new CommandResult { ExitCode = inspect }
                    : new CommandResult { ExitCode = build, StandardError = error });
            });
        return new SandboxImage(mock.Object);
    }

    private void WriteGlobal(string json) => File.WriteAllText(Path.Combine(_directory, "global.json"), json);

    [Theory]
    [InlineData(1)]
    [InlineData(125)]
    public async Task FailedBuildThrowsWithStandardError(int code)
    {
        WriteGlobal("""{"sdk":{"version":"10.0.100"}}""");
        var ex = await Assert.ThrowsAsync<SandboxImageException>(
            () => Image(1, code, "boom reason").EnsureAsync("t", _directory, TestContext.Current.CancellationToken));
        Assert.Contains("boom reason", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectExitOtherThanZeroOrOneBuilds()
    {
        WriteGlobal("""{"sdk":{"version":"10.0.100"}}""");
        await Image(125).EnsureAsync("t", _directory, TestContext.Current.CancellationToken);
        Assert.Equal(2, _recorded.Count);
        Assert.All(_recorded, s => Assert.Equal("docker", s.Command));
        Assert.Equal("build", _recorded[1].Arguments[0]);
    }

    [Fact]
    public async Task ExistingImageRunsOneCommand()
    {
        await Image(0).EnsureAsync("t", _directory, TestContext.Current.CancellationToken);
        Assert.Single(_recorded);
        Assert.DoesNotContain(_recorded, s => s.Arguments[0] is "build");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"sdk":{}}""")]
    [InlineData("""{}""")]
    public async Task MissingSdkVersionFailsBeforeBuild(string? json)
    {
        if (json is not null)
        {
            WriteGlobal(json);
        }

        await Assert.ThrowsAnyAsync<Exception>(
            () => Image(1).EnsureAsync("t", _directory, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_recorded, s => s.Arguments[0] is "build");
    }

    [Fact]
    public async Task TokenReachesEveryCall()
    {
        WriteGlobal("""{"sdk":{"version":"10.0.100"}}""");
        using var cts = new CancellationTokenSource();
        await Image(1).EnsureAsync("t", _directory, cts.Token);
        Assert.Equal(2, _tokens.Count);
        Assert.All(_tokens, t => Assert.Equal(cts.Token, t));
    }
}
