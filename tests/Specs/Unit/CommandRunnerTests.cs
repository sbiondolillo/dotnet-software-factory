using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task RunAsyncTimesOutNamesTheProgramAndTheTimeoutAndStopsIt()
    {
        var runner = new CommandRunner();
        // sleep 6: a distinct argv from the scenario's "sleep 5", so this
        // probe cannot see another test's process.
        var command = new Command("sh", ["-c", "sleep 6"], Timeout: TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(command, TestContext.Current.CancellationToken));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 second", exception.Message);
        Assert.False(ProcessProbe.IsRunning(["sh", "-c", "sleep 6"]));
    }

    [Fact]
    public async Task RunAsyncThrowsNamingTheProgramWhenItIsOnNoPath()
    {
        var runner = new CommandRunner();
        var command = new Command("definitely-not-a-real-program-xyz", []);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => runner.RunAsync(command, TestContext.Current.CancellationToken));

        Assert.Contains("definitely-not-a-real-program-xyz", exception.Message);
    }

    [Fact]
    public async Task ExitCodeCallerReturnsTheCannedExitCodeWithoutStartingAProgram()
    {
        var mock = new Mock<ICommandRunner>();
        mock
            .Setup(runner => runner.RunAsync(It.IsAny<Command>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new CommandResult(7, "", "")));
        var caller = new ExitCodeCaller(mock.Object, new Command("sh", ["-c", "sleep 7"]));

        var exitCode = await caller.RunAsync();

        Assert.Equal(7, exitCode);
        Assert.False(ProcessProbe.IsRunning(["sh", "-c", "sleep 7"]));
    }
}
