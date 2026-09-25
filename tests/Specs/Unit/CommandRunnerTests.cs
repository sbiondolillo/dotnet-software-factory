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

        var exception = await Assert.ThrowsAsync<System.TimeoutException>(() => runner.RunAsync(spec));

        Assert.Contains("sh", exception.Message);
        Assert.Contains("1 second", exception.Message);
        await AssertProgramGone(["sleep", "11"]);
    }

    [Fact]
    public async Task AProgramOnNoPathThrowsACommandStartExceptionThatNamesIt()
    {
        var runner = new CommandRunner();
        var exception = await Assert.ThrowsAsync<CommandStartException>(
            () => runner.RunAsync(new CommandSpec { Command = "factory-unit-missing-xyz" }));

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
