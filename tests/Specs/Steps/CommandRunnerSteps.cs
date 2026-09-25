using System.Diagnostics;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private string _fileName = "";
    private List<string> _arguments = [];
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _extraEnvironmentVariables = [];
    private readonly List<string> _inheritedVariablesSet = [];
    private TimeSpan? _timeout;

    private CommandResult? _result;
    private Exception? _exception;

    private Mock<ICommandRunner>? _mockRunner;
    private string? _callerReport;

    [Given("the program {string} with arguments {string} and {string}")]
    public void GivenTheProgramWithArguments(string fileName, string first, string second)
    {
        _fileName = fileName;
        _arguments = [first, second];
    }

    [Given("the program {string} with arguments {string}, {string} and {string} and {string}")]
    public void GivenTheProgramWithArguments(string fileName, string first, string second, string third, string fourth)
    {
        _fileName = fileName;
        _arguments = [first, second, third, fourth];
    }

    [Given("the program {string} with no arguments")]
    public void GivenTheProgramWithNoArguments(string fileName)
    {
        _fileName = fileName;
        _arguments = [];
    }

    [Given("the program {string}")]
    public void GivenTheProgram(string fileName)
    {
        _fileName = fileName;
        _arguments = [];
    }

    [Given("the working directory is {string}")]
    public void GivenTheWorkingDirectoryIs(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("an extra environment variable {string} set to {string}")]
    public void GivenAnExtraEnvironmentVariableSetTo(string name, string value) => _extraEnvironmentVariables[name] = value;

    [Given("an inherited environment variable {string} set to {string}")]
    public void GivenAnInheritedEnvironmentVariableSetTo(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _inheritedVariablesSet.Add(name);
    }

    [Given("a timeout of {int} second")]
    public void GivenATimeoutOfSecond(int seconds) => _timeout = TimeSpan.FromSeconds(seconds);

    [Given("a caller that uses a Moq mock of CommandRunner")]
    public void GivenACallerThatUsesAMoqMockOfCommandRunner() => _mockRunner = new Mock<ICommandRunner>();

    [Given("the mock's RunAsync returns a canned CommandResult with exit code {int}, standard output {string} and standard error {string}")]
    public void GivenTheMocksRunAsyncReturnsACannedCommandResultWithExitCodeStandardOutputAndStandardError(
        int exitCode, string standardOutput, string standardError)
    {
        var cannedResult = new CommandResult(exitCode, standardOutput, standardError);
        _mockRunner!
            .Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cannedResult);
    }

    [When("I run the program with CommandRunner")]
    public async Task WhenIRunTheProgramWithCommandRunner()
    {
        var spec = new CommandSpec
        {
            FileName = _fileName,
            Arguments = _arguments,
            WorkingDirectory = _workingDirectory,
            EnvironmentVariables = _extraEnvironmentVariables.Count == 0 ? null : _extraEnvironmentVariables,
            Timeout = _timeout,
        };

        var runner = new CommandRunner();

        try
        {
            _result = await runner.RunAsync(spec);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
        finally
        {
            foreach (var name in _inheritedVariablesSet)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    [When("the caller runs")]
    public async Task WhenTheCallerRuns()
    {
        var result = await _mockRunner!.Object.RunAsync(new CommandSpec { FileName = "unused" });
        _callerReport = result.ExitCode == 0
            ? $"success with \"{result.StandardOutput}\""
            : $"failure with exit code {result.ExitCode}";
    }

    [Then("the result's exit code is {int}")]
    public void ThenTheResultsExitCodeIs(int expected) => Assert.Equal(expected, _result!.ExitCode);

    [Then("the result's standard output is {string}")]
    public void ThenTheResultsStandardOutputIs(string expected) => Assert.Equal(expected, _result!.StandardOutput);

    [Then("the result's standard error is {string}")]
    public void ThenTheResultsStandardErrorIs(string expected) => Assert.Equal(expected, _result!.StandardError);

    [Then("the result's standard output does not contain {string}")]
    public void ThenTheResultsStandardOutputDoesNotContain(string unexpected) =>
        Assert.DoesNotContain(unexpected, _result!.StandardOutput, StringComparison.Ordinal);

    [Then("the result's standard output is exactly {int} bytes of {string}")]
    public void ThenTheResultsStandardOutputIsExactlyBytesOf(int byteCount, string filler)
    {
        Assert.Equal(byteCount, _result!.StandardOutput.Length);
        Assert.True(_result.StandardOutput.All(c => c == filler[0]));
    }

    [Then("the result's standard error is exactly {int} bytes of {string}")]
    public void ThenTheResultsStandardErrorIsExactlyBytesOf(int byteCount, string filler)
    {
        Assert.Equal(byteCount, _result!.StandardError.Length);
        Assert.True(_result.StandardError.All(c => c == filler[0]));
    }

    [Then("the result's standard output, trimmed, is {string}")]
    public void ThenTheResultsStandardOutputTrimmedIs(string expected) => Assert.Equal(expected, _result!.StandardOutput.Trim());

    [Then("RunAsync throws a TimeoutException whose message names {string} and {string}")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageNamesAnd(string program, string duration)
    {
        var timeoutException = Assert.IsType<TimeoutException>(_exception);
        Assert.Contains(program, timeoutException.Message, StringComparison.Ordinal);
        Assert.Contains(duration, timeoutException.Message, StringComparison.Ordinal);
    }

    [Then("the program is stopped")]
    public static async Task ThenTheProgramIsStopped()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(IsProcessRunning(["sh", "-c", "sleep 5"]));
    }

    /// <summary>Whether any process on the machine has exactly <paramref name="argv"/> as its command line,
    /// found by scanning <c>/proc</c> directly since this image has neither <c>pgrep</c> nor <c>ps</c>. Matching
    /// the whole argument vector, not a substring of it, keeps this from matching an unrelated process whose
    /// command line happens to quote this very scenario's shell command.</summary>
    private static bool IsProcessRunning(string[] argv)
    {
        foreach (var processDirectory in Directory.EnumerateDirectories("/proc"))
        {
            var pidText = Path.GetFileName(processDirectory);
            if (!int.TryParse(pidText, out _))
            {
                continue;
            }

            var cmdlinePath = Path.Combine(processDirectory, "cmdline");
            string cmdline;
            try
            {
                cmdline = File.ReadAllText(cmdlinePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var processArgv = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (processArgv.SequenceEqual(argv, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [Then("RunAsync throws an exception whose message names {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageNames(string program)
    {
        Assert.NotNull(_exception);
        Assert.Contains(program, _exception.Message, StringComparison.Ordinal);
    }

    [Then("the caller reports success with {string}")]
    public void ThenTheCallerReportsSuccessWith(string expected) => Assert.Equal($"success with \"{expected}\"", _callerReport);

    [Then("no program starts")]
    public void ThenNoProgramStarts() => _mockRunner!.Verify(
        r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()), Times.Once);
}
