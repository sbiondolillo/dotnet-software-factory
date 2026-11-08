using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class AgentBackendTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-backend-root-");
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-backend-tree-");
    private readonly RefusingHandler _handler = new();

    public void Dispose()
    {
        _handler.Dispose();
        _root.Delete(recursive: true);
        _tree.Delete(recursive: true);
    }

    [Fact]
    public async Task PickWithTheManagedSettingsStartsAManagedBackend()
    {
        await using AgentBackend backend = await StartAsync(ManagedRunFake.Settings());

        Assert.IsType<ManagedBackend>(backend);
    }

    [Fact]
    public async Task PickWithNoManagedSettingsStartsAClaudeCodeBackend()
    {
        await using AgentBackend backend = await StartAsync(null);

        Assert.IsType<ClaudeCodeBackend>(backend);
    }

    private async Task<AgentBackend> StartAsync(ManagedAgentsSettings? settings)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(
                spec.Arguments[0] is "run" ? new CommandResult { StandardOutput = "abc123\n" } : new CommandResult()));
        StartBackend start = AgentBackend.Pick(new BackendOptions(
            mock.Object, "t:1", _root.FullName, settings, new RepoName("owner", "repo"), "token", "Factory", "factory@example.com", _handler));
        return await start(new BackendStart(new Git(mock.Object), _tree.FullName, "base", "scratch", "Title", null), TestContext.Current.CancellationToken);
    }

    /// <summary>A handler that fails each request: the start of a backend sends none.</summary>
    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"The start sent a request to {request.RequestUri}.");
    }
}
