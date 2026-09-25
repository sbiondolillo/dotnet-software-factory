using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps : IDisposable
{
    private readonly List<(string Key, string? PreviousValue)> _inheritedEnvironmentChanges = [];

    private string _program = "";
    private readonly List<string> _arguments = [];
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _extraEnvironmentVariables = [];

    private CommandResult? _result;
    private Exception? _thrownException;

    private CommandResult? _cannedResult;
    private Mock<CommandRunner>? _mockRunner;
    private CommandResult? _callerResult;

    [Given("the program {string} with arguments {string} {string}")]
    public void GivenTheProgramWithArguments(string program, string firstArgument, string secondArgument)
    {
        _program = program;
        _arguments.Add(UnescapeQuotes(firstArgument));
        _arguments.Add(UnescapeQuotes(secondArgument));
    }

    // Reqnroll delivers a \" inside a double-quoted {string} match with its
    // backslash kept, as the two characters \". A step that wrote such an
    // argument meant a plain embedded ", so undo that escaping here before
    // the value reaches the child process as shell script text.
    private static string UnescapeQuotes(string value) => value.Replace("\\\"", "\"");

    [Given("the program {string} is run with these arguments")]
    public void GivenTheProgramIsRunWithTheseArguments(string program, DataTable table)
    {
        _program = program;
        foreach (var row in table.Rows)
        {
            _arguments.Add(row["argument"]);
        }
    }

    [Given("the program {string} with no arguments")]
    public void GivenTheProgramWithNoArguments(string program) => _program = program;

    [Given("the working directory {string}")]
    public void GivenTheWorkingDirectory(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("the extra environment variable {string} set to {string}")]
    public void GivenTheExtraEnvironmentVariableSetTo(string name, string value) =>
        _extraEnvironmentVariables[name] = value;

    [Given("the inherited environment variable {string} is set to {string}")]
    public void GivenTheInheritedEnvironmentVariableIsSetTo(string name, string value)
    {
        _inheritedEnvironmentChanges.Add((name, Environment.GetEnvironmentVariable(name)));
        Environment.SetEnvironmentVariable(name, value);
    }

    [Given("a Moq mock of CommandRunner whose RunAsync returns exit code {int}, standard output {string} and standard error {string}")]
    public void GivenAMoqMockOfCommandRunnerWhoseRunAsyncReturns(int exitCode, string standardOutput, string standardError)
    {
        _cannedResult = new CommandResult(exitCode, standardOutput, standardError);
        _mockRunner = new Mock<CommandRunner>();
        _mockRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(_cannedResult);
    }

    [When("I run the command with a timeout of {int} seconds")]
    public async Task WhenIRunTheCommandWithATimeoutOfSeconds(int seconds)
    {
        _result = await new CommandRunner()
            .RunAsync(_program, _arguments, _workingDirectory, _extraEnvironmentVariables, TimeSpan.FromSeconds(seconds));
    }

    [When("I run the command with a timeout of {int} seconds and it times out")]
    public async Task WhenIRunTheCommandWithATimeoutOfSecondsAndItTimesOut(int seconds)
    {
        try
        {
            _result = await new CommandRunner()
                .RunAsync(_program, _arguments, _workingDirectory, _extraEnvironmentVariables, TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            _thrownException = ex;
        }
    }

    [When("I run the command with a timeout of {int} seconds and it fails to start")]
    public async Task WhenIRunTheCommandWithATimeoutOfSecondsAndItFailsToStart(int seconds)
    {
        try
        {
            _result = await new CommandRunner()
                .RunAsync(_program, _arguments, _workingDirectory, _extraEnvironmentVariables, TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            _thrownException = ex;
        }
    }

    [When("the caller runs its command through the mocked CommandRunner")]
    public async Task WhenTheCallerRunsItsCommandThroughTheMockedCommandRunner()
    {
        Assert.NotNull(_mockRunner);
        _callerResult = await _mockRunner.Object.RunAsync(
            "irrelevant", Array.Empty<string>(), null, null, TimeSpan.FromSeconds(1));
    }

    [Then("the result's exit code is {int}")]
    public void ThenTheResultSExitCodeIs(int exitCode)
    {
        Assert.NotNull(_result);
        Assert.Equal(exitCode, _result.ExitCode);
    }

    [Then("the result's standard output is {string}")]
    public void ThenTheResultSStandardOutputIs(string expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result.StandardOutput);
    }

    [Then("the result's standard error is {string}")]
    public void ThenTheResultSStandardErrorIs(string expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result.StandardError);
    }

    [Then("the result's standard output contains {string}")]
    public void ThenTheResultSStandardOutputContains(string expected)
    {
        Assert.NotNull(_result);
        Assert.Contains(expected, _result.StandardOutput);
    }

    [Then("the result's standard output is {int} bytes of {string}")]
    public void ThenTheResultSStandardOutputIsBytesOf(int count, string character)
    {
        Assert.NotNull(_result);
        Assert.Equal(new string(character[0], count), _result.StandardOutput);
    }

    [Then("the result's standard error is {int} bytes of {string}")]
    public void ThenTheResultSStandardErrorIsBytesOf(int count, string character)
    {
        Assert.NotNull(_result);
        Assert.Equal(new string(character[0], count), _result.StandardError);
    }

    [Then("RunAsync throws a TimeoutException whose message names the program {string} and the timeout of {int} seconds")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageNamesTheProgramAndTheTimeoutOfSeconds(string program, int seconds)
    {
        var exception = Assert.IsType<TimeoutException>(_thrownException);
        Assert.Contains(program, exception.Message);
        Assert.Contains($"{seconds} seconds", exception.Message);
    }

    [Then("RunAsync throws an exception whose message names the program {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageNamesTheProgram(string program)
    {
        Assert.NotNull(_thrownException);
        Assert.Contains(program, _thrownException.Message);
    }

    [Then("the caller's result matches the canned result")]
    public void ThenTheCallerSResultMatchesTheCannedResult() => Assert.Equal(_cannedResult, _callerResult);

    [Then("no program is started")]
    public void ThenNoProgramIsStarted()
    {
        Assert.NotNull(_mockRunner);
        _mockRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<TimeSpan>()),
            Times.Once());
    }

    public void Dispose()
    {
        foreach (var (key, previousValue) in _inheritedEnvironmentChanges)
        {
            Environment.SetEnvironmentVariable(key, previousValue);
        }
    }
}
