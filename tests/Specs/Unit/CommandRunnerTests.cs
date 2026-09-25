using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task RunAsyncThrowsTimeoutExceptionAndKillsAnOutlivingProcess()
    {
        var timeout = TimeSpan.FromMilliseconds(100);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            CommandRunner.RunAsync("sh", ["-c", "sleep 5"], timeout: timeout));

        Assert.Contains("sh", exception.Message, StringComparison.Ordinal);
        Assert.Contains(timeout.ToString("c"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncThrowsWhenTheProgramIsNotOnPath()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            CommandRunner.RunAsync("no-such-program-xyz", []));

        Assert.Contains("no-such-program-xyz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtraEnvironmentVariableReplacesAnInheritedVariableOfTheSameName()
    {
        Environment.SetEnvironmentVariable("COMMAND_RUNNER_TEST_HOME", "original");
        try
        {
            var result = await CommandRunner.RunAsync(
                "sh",
                ["-c", "printf \"%s\" \"$COMMAND_RUNNER_TEST_HOME\""],
                environmentVariables: new Dictionary<string, string>
                {
                    ["COMMAND_RUNNER_TEST_HOME"] = "override",
                });

            Assert.Equal("override", result.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMMAND_RUNNER_TEST_HOME", null);
        }
    }

    [Fact]
    public async Task AnInheritedVariableTheExtraVariablesLeaveOutKeepsItsInheritedValue()
    {
        Environment.SetEnvironmentVariable("COMMAND_RUNNER_TEST_HOME", "kept");
        try
        {
            var result = await CommandRunner.RunAsync(
                "sh",
                ["-c", "printf \"%s\" \"$COMMAND_RUNNER_TEST_HOME\""],
                environmentVariables: new Dictionary<string, string> { ["FOO"] = "bar" });

            Assert.Equal("kept", result.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMMAND_RUNNER_TEST_HOME", null);
        }
    }

    [Fact]
    public async Task StandardOutputAndStandardErrorAreCapturedIndependentlyWithoutDeadlock()
    {
        var result = await CommandRunner.RunAsync(
            "sh",
            [
                "-c",
                "head -c 1048576 /dev/zero | tr '\\0' 'a'; " +
                "head -c 1048576 /dev/zero | tr '\\0' 'b' 1>&2",
            ]);

        Assert.Equal(1048576, result.StandardOutput.Length);
        Assert.All(result.StandardOutput, c => Assert.Equal('a', c));
        Assert.Equal(1048576, result.StandardError.Length);
        Assert.All(result.StandardError, c => Assert.Equal('b', c));
    }

    [Fact]
    public async Task AnArgumentWithASpaceADoubleQuoteAndADollarSignReachesTheProgramAsOneArgument()
    {
        var result = await CommandRunner.RunAsync(
            "sh",
            ["-c", "printf \"%s\" \"$1\"", "sh", "a \"b\" $c"]);

        Assert.Equal("a \"b\" $c", result.StandardOutput);
    }
}
