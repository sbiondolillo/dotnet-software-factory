using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps(ExceptionState exceptionState, ConsoleState consoleState)
{
    private static readonly Regex QuotedSpan = new(@"""[^""]*""|'[^']*'", RegexOptions.Compiled);

    private readonly CommandRunner _runner = new();
    private readonly Dictionary<string, string> _extraEnvironment = [];
    private Mock<CommandRunner>? _mock;

    private string? _command;
    private string[] _arguments = [];
    private string? _workingDirectory;
    private TimeSpan? _timeout;
    private CommandResult? _result;
    private CommandResult? _mockResult;
    private string? _mockedCommand;
    private Task<CommandResult>? _leftRunningTask;

    [Given(@"^the command is (""[^""]*""|'[^']*')$")]
    public void GivenTheCommandIs(string command)
    {
        _command = Unquote(command);
        _arguments = [];
    }

    [Given(@"^the command is (""[^""]*""|'[^']*') with arguments (.+)$")]
    public void GivenTheCommandIsWithArguments(string command, string tail)
    {
        _command = Unquote(command);
        _arguments = [.. QuotedSpan.Matches(tail).Select(match => Unquote(match.Value))];
    }

    [Given("the working directory is {string}")]
    public void GivenTheWorkingDirectoryIs(string directory)
    {
        Directory.CreateDirectory(directory);
        _workingDirectory = directory;
    }

    [Given("the extra environment variable {string} is {string}")]
    public void GivenTheExtraEnvironmentVariableIs(string name, string value) => _extraEnvironment[name] = value;

    [Given("the inherited environment variable {string} is {string}")]
    public static void GivenTheInheritedEnvironmentVariableIs(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    [Given("the timeout is {int} seconds")]
    public void GivenTheTimeoutIs(int seconds) => _timeout = TimeSpan.FromSeconds(seconds);

    [Given("a caller receives a mock of CommandRunner")]
    public void GivenACallerReceivesAMockOfCommandRunner() => _mock = new Mock<CommandRunner>(MockBehavior.Strict);

    [Given("the mock returns a CommandResult with exit code {int} and standard output {string}")]
    public void GivenTheMockReturns(int exitCode, string standardOutput)
        => _mock!.Setup(mock => mock.RunAsync(It.IsAny<CommandSpec>()))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput });

    [When("I run the command")]
    public async Task WhenIRunTheCommand()
    {
        try
        {
            _result = await _runner.RunAsync(BuildSpec());
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            _result = null;
            exceptionState.Exception = ex;
        }
    }

    [When("I run the command and cancel the token {int} second after the call starts")]
    public async Task WhenIRunTheCommandAndCancelTheTokenSecondAfterTheCallStarts(int seconds)
    {
        using var cts = new CancellationTokenSource();
        Task<CommandResult> task = _runner.RunAsync(BuildSpec(), cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        cts.Cancel();

        try
        {
            _result = await task;
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            _result = null;
            exceptionState.Exception = ex;
        }
    }

    [When("I start the command and wait until the {string} program runs")]
    public async Task WhenIStartTheCommandAndWaitUntilTheProgramRuns(string program)
    {
        _leftRunningTask = _runner.RunAsync(BuildSpec());
        var clock = Stopwatch.StartNew();
        while (!ProcessScan.HasProcessWithExactArgv(program.Split(' ')))
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"The program {program} did not start within 5 s");
            await Task.Delay(50);
        }
    }

    [AfterScenario]
    public async Task StopTheCommandLeftRunning()
    {
        if (_leftRunningTask is not null)
        {
            await Assert.ThrowsAsync<System.TimeoutException>(() => _leftRunningTask);
        }
    }

    [When("I run the command with a token that is already cancelled")]
    public async Task WhenIRunTheCommandWithATokenThatIsAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            _result = await _runner.RunAsync(BuildSpec(), cts.Token);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            _result = null;
            exceptionState.Exception = ex;
        }
    }

    private CommandSpec BuildSpec() => new()
    {
        Command = _command ?? string.Empty,
        Arguments = _arguments,
        WorkingDirectory = _workingDirectory,
        EnvironmentVariables = _extraEnvironment,
        Timeout = _timeout,
    };

    [When("the caller asks the mock to run {string}")]
    public async Task WhenTheCallerAsksTheMockToRun(string command)
    {
        _mockedCommand = command;
        _mockResult = await _mock!.Object.RunAsync(new CommandSpec { Command = command });
    }

    [Then("the exit code is {int}")]
    public void ThenTheExitCodeIs(int expected)
    {
        if (consoleState.ExitCode is int consoleCode)
        {
            Assert.Equal(expected, consoleCode);
            return;
        }

        Assert.NotNull(_result);
        Assert.Equal(expected, _result.ExitCode);
    }

    [Then("the standard output contains {string}")]
    public void ThenTheStandardOutputContains(string text) => Assert.Contains(text, StandardOutput(), StringComparison.Ordinal);

    [Then("the standard output does not contain {string}")]
    public void ThenTheStandardOutputDoesNotContain(string text) => Assert.DoesNotContain(text, StandardOutput(), StringComparison.Ordinal);

    [Then("the standard error contains {string}")]
    public void ThenTheStandardErrorContains(string text) => Assert.Contains(text, StandardError(), StringComparison.Ordinal);

    [Then("the standard error does not contain {string}")]
    public void ThenTheStandardErrorDoesNotContain(string text) => Assert.DoesNotContain(text, StandardError(), StringComparison.Ordinal);

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected) => Assert.Equal(expected, StandardOutput());

    [Then("the standard output is {int} bytes")]
    public void ThenTheStandardOutputIsBytes(int count) => Assert.Equal(count, Encoding.UTF8.GetByteCount(StandardOutput()));

    [Then("the standard error is {int} bytes")]
    public void ThenTheStandardErrorIsBytes(int count) => Assert.Equal(count, Encoding.UTF8.GetByteCount(StandardError()));

    [Then("the standard output is the letter {word} repeated {int} times")]
    public void ThenTheStandardOutputIsTheLetterRepeated(string letter, int count) => Assert.Equal(new string(letter[0], count), StandardOutput());

    [Then("the standard error is the letter {word} repeated {int} times")]
    public void ThenTheStandardErrorIsTheLetterRepeated(string letter, int count) => Assert.Equal(new string(letter[0], count), StandardError());

    [Then("RunAsync throws a TimeoutException")]
    public void ThenRunAsyncThrowsATimeoutException() => Assert.IsType<System.TimeoutException>(exceptionState.Exception);

    [Then("RunAsync throws an OperationCanceledException")]
    public void ThenRunAsyncThrowsAnOperationCanceledException()
        => Assert.IsAssignableFrom<OperationCanceledException>(exceptionState.Exception);

    [Then("the file {string} does not exist")]
    public static void ThenTheFileDoesNotExist(string path) => Assert.False(File.Exists(path));

    [Then("RunAsync throws an exception")]
    public void ThenRunAsyncThrowsAnException() => Assert.NotNull(exceptionState.Exception);

    [Then("the exception message contains {string}")]
    public void ThenTheExceptionMessageContains(string text)
    {
        Assert.NotNull(exceptionState.Exception);
        Assert.Contains(text, exceptionState.Exception.Message, StringComparison.Ordinal);
    }

    [Then("the {string} program is no longer running")]
    public static Task ThenTheProgramIsNoLongerRunning(string program) => ProcessScan.AssertGoneAsync(program.Split(' '));

    [Then("no program was started")]
    public Task ThenNoProgramWasStarted() => ProcessScan.AssertGoneAsync((_mockedCommand ?? string.Empty).Split(' '));

    [Then("the caller sees the exit code {int}")]
    public void ThenTheCallerSeesTheExitCode(int expected)
    {
        Assert.NotNull(_mockResult);
        Assert.Equal(expected, _mockResult.ExitCode);
    }

    [Then("the caller sees the standard output {string}")]
    public void ThenTheCallerSeesTheStandardOutput(string expected)
    {
        Assert.NotNull(_mockResult);
        Assert.Equal(expected, _mockResult.StandardOutput);
    }

    private string StandardOutput()
    {
        Assert.NotNull(_result);
        return _result.StandardOutput;
    }

    private string StandardError()
    {
        Assert.NotNull(_result);
        return _result.StandardError;
    }

    private static string Unquote(string quoted) => quoted[1..^1];
}
