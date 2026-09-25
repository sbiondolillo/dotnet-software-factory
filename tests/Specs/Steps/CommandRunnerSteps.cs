using System.Text;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private readonly CommandRunner _runner = new();
    private readonly Dictionary<string, string> _extraEnvironmentVariables = new();
    private readonly Dictionary<string, string?> _ambientRestoration = new();

    private string _workingDirectory = "";
    private string _program = "";
    private IReadOnlyList<string> _arguments = Array.Empty<string>();
    private CommandResult? _result;
    private Exception? _failure;
    private TimeoutException? _timeoutException;
    private Mock<CommandRunner>? _mock;
    private CommandResult? _cannedResult;
    private CommandResult? _callerResult;

    [Given("the working directory {string}")]
    public void GivenTheWorkingDirectory(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("the extra environment variable {string} set to {string}")]
    public void GivenTheExtraEnvironmentVariable(string name, string value) => _extraEnvironmentVariables[name] = value;

    [Given("the environment already has {string} set to {string}")]
    public void GivenTheEnvironmentAlreadyHas(string name, string value)
    {
        if (!_ambientRestoration.ContainsKey(name))
        {
            _ambientRestoration[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    [Given("no extra environment variables")]
#pragma warning disable CA1822 // A step definition must be an instance method on the binding.
    public void GivenNoExtraEnvironmentVariables()
    {
        // Nothing to do: the extras dictionary stays empty.
    }
#pragma warning restore CA1822

    [Given(
        "a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code {int}, standard output {string} and standard error {string}")]
    public void GivenAMoqMockOfCommandRunner(int exitCode, string standardOutput, string standardError)
    {
        var canned = new CommandResult(exitCode, standardOutput, standardError);
        _cannedResult = canned;
        _mock = new Mock<CommandRunner>();
        _mock
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(canned));
    }

    [When("I run {string} with no arguments")]
    public Task WhenIRunWithNoArguments(string fileName) => Run(fileName, Array.Empty<string>(), null);

    [When("I run {string} with arguments {string} {string}")]
    public Task WhenIRunWithTwoArguments(string fileName, string first, string second) =>
        Run(fileName, [first, second], null);

    [When("I run {string} with arguments {string} {string} {string} {string}")]
    public Task WhenIRunWithFourArguments(string fileName, string first, string second, string third, string fourth) =>
        Run(fileName, [first, second, third, fourth], null);

    [When("I run {string} with arguments {string} {string} and a timeout of {int} second")]
    public Task WhenIRunWithTwoArgumentsAndATimeout(string fileName, string first, string second, int seconds) =>
        Run(fileName, [first, second], TimeSpan.FromSeconds(seconds));

    [When("the caller under test asks the mocked CommandRunner to run")]
    public async Task WhenTheCallerUnderTestAsksTheMockedCommandRunnerToRun()
    {
        _callerResult = await new Caller(_mock!.Object).RunAsync();
    }

    [Then("the exit code is {int}")]
    public void ThenTheExitCodeIs(int expected)
    {
        var result = Assert.IsType<CommandResult>(_result);
        Assert.Equal(expected, result.ExitCode);
    }

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected)
    {
        var result = Assert.IsType<CommandResult>(_result);
        Assert.Equal(Unescape(expected), result.StandardOutput);
    }

    [Then("the standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected)
    {
        var result = Assert.IsType<CommandResult>(_result);
        Assert.Equal(Unescape(expected), result.StandardError);
    }

    [Then("the standard output is {int} bytes long")]
    public void ThenTheStandardOutputIsBytesLong(int expectedBytes)
    {
        var result = Assert.IsType<CommandResult>(_result);
        Assert.Equal(expectedBytes, Encoding.UTF8.GetByteCount(result.StandardOutput));
    }

    [Then("the standard error is {int} bytes long")]
    public void ThenTheStandardErrorIsBytesLong(int expectedBytes)
    {
        var result = Assert.IsType<CommandResult>(_result);
        Assert.Equal(expectedBytes, Encoding.UTF8.GetByteCount(result.StandardError));
    }

    [Then("RunAsync throws a TimeoutException whose message contains {string}")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageContains(string fragment)
    {
        var timeout = Assert.IsType<TimeoutException>(_failure);
        _timeoutException = timeout;
        Assert.Contains(fragment, timeout.Message);
    }

    [Then("that TimeoutException's message contains {string}")]
    public void ThenThatTimeoutExceptionsMessageContains(string fragment)
    {
        var timeout = Assert.IsType<TimeoutException>(_timeoutException);
        Assert.Contains(fragment, timeout.Message);
    }

    [Then("the {string} process is no longer running")]
    public void ThenTheProcessIsNoLongerRunning(string program)
    {
        Assert.Equal(_program, program);
        string[] argv = [_program, .. _arguments];
        Assert.True(WaitUntilGone(argv, TimeSpan.FromSeconds(2)), "the process is still running");
    }

    [Then("RunAsync throws an exception whose message contains {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageContains(string fragment)
    {
        Assert.NotNull(_failure);
        var failure = _failure!;
        Assert.Contains(fragment, failure.Message);
    }

    [Then("the caller's result is the canned CommandResult")]
    public void ThenTheCallersResultIsTheCannedCommandResult()
    {
        Assert.Equal(_cannedResult, _callerResult);
    }

    [Then("no program was started")]
    public void ThenNoProgramWasStarted()
    {
        _mock!.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [AfterScenario]
    public void AfterScenario()
    {
        foreach (var (name, original) in _ambientRestoration)
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    private Task Run(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout)
    {
        _program = fileName;
        _arguments = arguments;
        return RunAsync(fileName, arguments, timeout);
    }

    private async Task RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout)
    {
        try
        {
            _result = await _runner.RunAsync(
                _workingDirectory,
                fileName,
                arguments,
                _extraEnvironmentVariables,
                timeout,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _failure = ex;
        }
    }

    // A pinned "\n" in the step text arrives as the two characters backslash-n:
    // this pipeline does no escape processing of the quoted value. The scenario
    // means an actual line break, so translate it before comparing.
    private static string Unescape(string expected) => expected.Replace("\\n", "\n");

    private static bool WaitUntilGone(IReadOnlyList<string> argv, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsRunning(argv) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        return !IsRunning(argv);
    }

    private static bool IsRunning(IReadOnlyList<string> argv)
    {
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var pid = entry.TrimEnd('/');
            if (!long.TryParse(pid, out _))
            {
                continue;
            }

            string[] processArgv;
            try
            {
                processArgv = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException)
            {
                continue; // The process exited between the listing and the read.
            }

            // An exact argv match: a substring scan would match the test host
            // itself, whose command line carries this session's prompt text.
            if (processArgv.SequenceEqual(argv))
            {
                return true;
            }
        }

        return false;
    }

    // A stand-in for any consumer that depends on CommandRunner.
    private sealed class Caller(CommandRunner runner)
    {
        public Task<CommandResult> RunAsync() => runner.RunAsync("/tmp", "true", Array.Empty<string>());
    }
}
