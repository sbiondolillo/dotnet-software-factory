using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class SandboxImageTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-sandbox-unit-");
    private readonly List<CommandSpec> _recorded = [];

    public void Dispose() => _directory.Delete(recursive: true);

    private SandboxImage NewImage(int inspectExit, int buildExit = 0, string buildError = "")
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(spec.Arguments[0] == "image"
                    ? new CommandResult { ExitCode = inspectExit }
                    : new CommandResult { ExitCode = buildExit, StandardError = buildError });
            });
        return new SandboxImage(mock.Object);
    }

    private void WriteGlobalJson(string content) => File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), content);

    [Theory]
    [InlineData(1)]
    [InlineData(125)]
    public async Task AnInspectExitOtherThanZeroLeadsToTheBuild(int inspectExit)
    {
        WriteGlobalJson("""{"sdk":{"version":"10.0.100"}}""");

        await NewImage(inspectExit).EnsureAsync("t:1", _directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(2, _recorded.Count);
        Assert.Equal("build", _recorded[1].Arguments[0]);
    }

    [Fact]
    public async Task AnExistingImageRunsOneCommandAndNeedsNoGlobalJson()
    {
        await NewImage(0).EnsureAsync("t:1", _directory.FullName, TestContext.Current.CancellationToken);

        Assert.Single(_recorded);
    }

    [Theory]
    [InlineData(1, "boom")]
    [InlineData(3, "")]
    public async Task ABuildExitOtherThanZeroThrowsWithTheStandardError(int buildExit, string error)
    {
        WriteGlobalJson("""{"sdk":{"version":"10.0.100"}}""");

        var exception = await Assert.ThrowsAsync<SandboxImageException>(
            () => NewImage(1, buildExit, error).EnsureAsync("t:1", _directory.FullName, TestContext.Current.CancellationToken));

        Assert.Contains(error, exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"sdk":{}}""")]
    [InlineData("not json")]
    public async Task ABadOrMissingGlobalJsonThrowsAndRunsNoBuild(string? content)
    {
        if (content is not null)
        {
            WriteGlobalJson(content);
        }

        await Assert.ThrowsAnyAsync<Exception>(
            () => NewImage(1).EnsureAsync("t:1", _directory.FullName, TestContext.Current.CancellationToken));

        Assert.Single(_recorded);
    }

    [Fact]
    public async Task ACancelledTokenThrowsOperationCanceledAndRunsNoCommandAfterIt()
    {
        WriteGlobalJson("""{"sdk":{"version":"10.0.100"}}""");
        using var cts = new CancellationTokenSource();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                cts.Cancel();
                return Task.FromResult(new CommandResult { ExitCode = 1 });
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SandboxImage(mock.Object).EnsureAsync("t:1", _directory.FullName, cts.Token));

        Assert.Single(_recorded);
    }
}
