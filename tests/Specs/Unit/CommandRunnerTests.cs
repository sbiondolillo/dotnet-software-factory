using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    private readonly CommandRunner _runner = new();

    [Fact]
    public async Task AProgramThatOutlivesItsTimeoutIsStoppedAndReported()
    {
        var request = new CommandRequest("sh", ["-c", "sleep 7"], null, new Dictionary<string, string>(), TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => _runner.RunAsync(request));

        Assert.Contains("sh", ex.Message);
        Assert.Contains("1 second", ex.Message);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while ((ProcessProbe.IsRunning("sh", "-c", "sleep 7") || ProcessProbe.IsRunning("sleep", "7")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(ProcessProbe.IsRunning("sh", "-c", "sleep 7"));
        Assert.False(ProcessProbe.IsRunning("sleep", "7"));
    }

    [Fact]
    public async Task AProgramOnNoPathFailsWithAMessageNamingIt()
    {
        var request = new CommandRequest("definitely-not-a-real-program-xyz", [], null, new Dictionary<string, string>(), null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _runner.RunAsync(request));

        Assert.Contains("definitely-not-a-real-program-xyz", ex.Message);
    }

    [Fact]
    public async Task ACallerTestedAgainstAMockObservesTheCannedResultAndStartsNoProgram()
    {
        var canned = new CommandResult(0, "canned-output", "");
        var mock = new Mock<ICommandRunner>();
        mock.Setup(r => r.RunAsync(It.IsAny<CommandRequest>())).ReturnsAsync(canned);

        var result = await mock.Object.RunAsync(new CommandRequest("sh", ["-c", "true"], null, new Dictionary<string, string>(), null));

        Assert.Same(canned, result);
        mock.Verify(r => r.RunAsync(It.IsAny<CommandRequest>()), Times.Once());
    }

    [Fact]
    public async Task AProgramThatExitsWithCodeThreeReportsThreeWithoutAnException()
    {
        var result = await _runner.RunAsync(new CommandRequest("sh", ["-c", "exit 3"], null, new Dictionary<string, string>(), null));

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task StandardOutputAndStandardErrorArriveApart()
    {
        var result = await _runner.RunAsync(new CommandRequest("sh", ["-c", "printf out; printf err 1>&2"], null, new Dictionary<string, string>(), null));

        Assert.Equal("out", result.StandardOutput);
        Assert.Equal("err", result.StandardError);
        Assert.DoesNotContain("err", result.StandardOutput);
    }

    [Fact]
    public async Task AMegabyteOnEachStreamArrivesWhole()
    {
        var script = "head -c 1048576 /dev/zero | tr '\\0' y; head -c 1048576 /dev/zero | tr '\\0' y 1>&2";
        var result = await _runner.RunAsync(new CommandRequest("sh", ["-c", script], null, new Dictionary<string, string>(), null));

        Assert.Equal(1048576, result.StandardOutput.Length);
        Assert.Equal(1048576, result.StandardError.Length);
    }

    [Fact]
    public async Task AnArgumentWithAQuoteAndADollarSignArrivesAsOneArgument()
    {
        var result = await _runner.RunAsync(new CommandRequest("printf", ["%s", "a \"b\" $c"], null, new Dictionary<string, string>(), null));

        Assert.Equal("a \"b\" $c", result.StandardOutput);
    }

    [Fact]
    public async Task TheProgramRunsInTheGivenWorkingDirectory()
    {
        var result = await _runner.RunAsync(new CommandRequest("pwd", [], "/tmp", new Dictionary<string, string>(), null));

        Assert.Equal("/tmp\n", result.StandardOutput);
    }

    [Fact]
    public async Task AnExtraEnvironmentVariableIsVisibleReplacesAndKeeps()
    {
        const string extra = "FACTORY_UNIT_ENV_EXTRA";
        const string inherited = "FACTORY_UNIT_ENV_INHERITED";
        const string replaced = "FACTORY_UNIT_ENV_REPLACED";

        try
        {
            Environment.SetEnvironmentVariable(inherited, "keep-me");
            Environment.SetEnvironmentVariable(replaced, "old-value");

            var script = $"printf '%s %s %s' \"$FACTORY_UNIT_ENV_EXTRA\" \"$FACTORY_UNIT_ENV_INHERITED\" \"$FACTORY_UNIT_ENV_REPLACED\"";
            var result = await _runner.RunAsync(new CommandRequest(
                "sh",
                ["-c", script],
                null,
                new Dictionary<string, string>
                {
                    [extra] = "extra",
                    [replaced] = "new-value",
                },
                null));

            Assert.Equal("extra keep-me new-value", result.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable(extra, null);
            Environment.SetEnvironmentVariable(inherited, null);
            Environment.SetEnvironmentVariable(replaced, null);
        }
    }
}
