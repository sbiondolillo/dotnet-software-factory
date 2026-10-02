using System.Text.Json;
using Core;
using Moq;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class HeadBranchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task AFailedReadOfTheHeadBranchThrowsWithTheStatus(int status)
    {
        var handler = new RecordingHandler();
        handler.Answer(status, "{\"message\":\"x\"}");
        var sut = new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero));
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadHeadBranchAsync("o", "r", 10, Ct));
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task TheHeadBranchComesBackWholeFromThePullRequestAddress()
    {
        var handler = new RecordingHandler();
        handler.Answer(200, JsonSerializer.Serialize(new { head = new { @ref = "factory/a1b2c3d4" } }));
        var sut = new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero));
        Assert.Equal("factory/a1b2c3d4", await sut.ReadHeadBranchAsync("o", "r", 10, Ct));
        Assert.EndsWith("/repos/o/r/pulls/10", Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task ACancelledReadOfTheHeadBranchLetsOperationCanceledExceptionOutUnwrapped()
    {
        var handler = new RecordingHandler { Throw = new OperationCanceledException() };
        var sut = new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ReadHeadBranchAsync("o", "r", 10, Ct));
    }

    [Fact]
    public async Task TheCheckoutRunsGitCheckoutWithoutDashBInTheCloneDirectory()
    {
        CommandSpec? seen = null;
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                seen = spec;
                return Task.FromResult(new CommandResult());
            });
        await new Git(mock.Object).CheckoutExistingBranchAsync("factory/a1b2c3d4", "/tmp/clone", Ct);
        Assert.Equal("git", seen!.Command);
        Assert.Equal(["checkout", "factory/a1b2c3d4"], seen.Arguments);
        Assert.Equal("/tmp/clone", seen.WorkingDirectory);
    }

    [Fact]
    public async Task ACheckoutThatExitsWithTheCodeOneThrowsNamingCheckoutAndHoldingTheStandardError()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 1, StandardError = "did not match any file(s) known to git" });
        var ex = await Assert.ThrowsAsync<GitCommandException>(() => new Git(mock.Object).CheckoutExistingBranchAsync("b", "/tmp/clone", Ct));
        Assert.Contains("checkout", ex.Message);
        Assert.Contains("did not match any file(s) known to git", ex.Message);
    }

    [Fact]
    public async Task ACancelledCheckoutLetsOperationCanceledExceptionReachTheCallerUnwrapped()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec _, CancellationToken _) => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => new Git(mock.Object).CheckoutExistingBranchAsync("b", "/tmp/clone", Ct));
    }
}
