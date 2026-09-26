using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task ATimeoutShorterThanTheProgramThrowsAndStopsTheWholeProcessTree()
    {
        var runner = new CommandRunner();
        var spec = new CommandSpec
        {
            Command = "sh",
            Arguments = ["-c", "sleep 11"],
            Timeout = TimeSpan.FromSeconds(1),
        };

        var exception = await Assert.ThrowsAsync<System.TimeoutException>(() => runner.RunAsync(spec, TestContext.Current.CancellationToken));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 second", exception.Message);
        await AssertProgramGone(["sleep", "11"]);
    }

    [Fact]
    public async Task CancellingTheTokenWithNoTimeoutSetThrowsAndStopsTheWholeProcessTree()
    {
        var runner = new CommandRunner();
        var spec = new CommandSpec { Command = "sh", Arguments = ["-c", "sleep 14"] };
        using var cts = new CancellationTokenSource();

        Task<CommandResult> task = runner.RunAsync(spec, cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await AssertProgramGone(["sleep", "14"]);
    }

    [Fact]
    public async Task ATokenCancelledBeforeTheCallThrowsAndNeverStartsAProgram()
    {
        string marker = "/tmp/factory-cancel-marker-unit";
        File.Delete(marker);
        var runner = new CommandRunner();
        var spec = new CommandSpec { Command = "sh", Arguments = ["-c", $"touch {marker}"] };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(spec, cts.Token));

        Assert.False(ProcessScan.HasProcessWithExactArgv(["sh", "-c", $"touch {marker}"]));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ATokenThatFiresBeforeALongerTimeoutThrowsOperationCanceledExceptionNotTimeoutException()
    {
        var runner = new CommandRunner();
        var spec = new CommandSpec
        {
            Command = "sh",
            Arguments = ["-c", "sleep 15"],
            Timeout = TimeSpan.FromSeconds(10),
        };
        using var cts = new CancellationTokenSource();

        Task<CommandResult> task = runner.RunAsync(spec, cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        await AssertProgramGone(["sleep", "15"]);
    }

    [Fact]
    public async Task AProgramOnNoPathThrowsACommandStartExceptionThatNamesIt()
    {
        var runner = new CommandRunner();
        var exception = await Assert.ThrowsAsync<CommandStartException>(
            () => runner.RunAsync(new CommandSpec { Command = "factory-unit-missing-xyz" }, TestContext.Current.CancellationToken));

        Assert.Contains("factory-unit-missing-xyz", exception.Message);
    }

    private static async Task AssertProgramGone(string[] argv)
    {
        for (int attempt = 0; attempt < 20 && ProcessScan.HasProcessWithExactArgv(argv); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.False(ProcessScan.HasProcessWithExactArgv(argv));
    }
}
