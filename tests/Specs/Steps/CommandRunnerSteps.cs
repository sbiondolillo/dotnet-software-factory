using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private string _program = "";
    private readonly List<string> _arguments = [];
    private string? _workingDirectory;
    private Dictionary<string, string>? _environmentVariables;
    private int? _timeoutSeconds;
    private readonly List<string> _envVarsToRestore = [];
    private readonly Dictionary<string, string?> _originalEnvironmentValues = [];

    private CommandResult? _result;
    private Exception? _exception;

    private Mock<CommandRunner>? _mock;

    [Given("the program {string}")]
    public void GivenTheProgram(string program) => _program = program;

    [Given("the program {string} with no arguments")]
    public void GivenTheProgramWithNoArguments(string program) => _program = program;

    [Given("the arguments")]
    public void GivenTheArguments(Table table)
    {
        var columnName = table.Header.First();
        _arguments.Add(columnName);
        foreach (var row in table.Rows)
        {
            _arguments.Add(row[columnName]);
        }
    }

    [Given("the working directory {string}")]
    public void GivenTheWorkingDirectory(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("the extra environment variable {string} set to {string}")]
    public void GivenTheExtraEnvironmentVariableSetTo(string name, string value)
    {
        _environmentVariables ??= [];
        _environmentVariables[name] = value;
    }

    [Given("the timeout is {int} second")]
    public void GivenTheTimeoutIsSecond(int seconds) => _timeoutSeconds = seconds;

    [Given("the current process has an environment variable named {string} set to {string}")]
    public void GivenTheCurrentProcessHasAnEnvironmentVariableNamedSetTo(string name, string value)
    {
        _originalEnvironmentValues[name] = Environment.GetEnvironmentVariable(name);
        _envVarsToRestore.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    [When("I run the command")]
    public void WhenIRunTheCommand()
    {
        var request = new CommandRequest
        {
            Program = _program,
            Arguments = _arguments,
            WorkingDirectory = _workingDirectory,
            EnvironmentVariables = _environmentVariables,
            Timeout = _timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        };

        try
        {
            _result = new CommandRunner().RunAsync(request).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("the result's exit code is {int}")]
    public void ThenTheResultsExitCodeIs(int expected) => Assert.Equal(expected, _result!.ExitCode);

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected) => Assert.Equal(expected, _result!.StandardOutput);

    [Then("the standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected) => Assert.Equal(expected, _result!.StandardError);

    [Then("the standard output is {int} bytes of {string}")]
    public void ThenTheStandardOutputIsBytesOf(int count, string character) =>
        Assert.Equal(new string(character[0], count), _result!.StandardOutput);

    [Then("the standard error is {int} bytes of {string}")]
    public void ThenTheStandardErrorIsBytesOf(int count, string character) =>
        Assert.Equal(new string(character[0], count), _result!.StandardError);

    [Then("the standard output contains {string}")]
    public void ThenTheStandardOutputContains(string expected) => Assert.Contains(expected, _result!.StandardOutput);

    [Then("the standard output does not contain {string}")]
    public void ThenTheStandardOutputDoesNotContain(string expected) =>
        Assert.DoesNotContain(expected, _result!.StandardOutput);

    [Then("running the command raises a {string}")]
    public void ThenRunningTheCommandRaisesA(string typeName)
    {
        Assert.NotNull(_exception);
        Assert.Equal(typeName, _exception.GetType().Name);
    }

    [Then("running the command raises an exception")]
    public void ThenRunningTheCommandRaisesAnException() => Assert.NotNull(_exception);

    [Then("the exception message contains {string}")]
    public void ThenTheExceptionMessageContains(string expected) => Assert.Contains(expected, _exception!.Message);

    [Given("a Moq mock of {string} whose {string} returns a canned result with exit code {int} and standard output {string}")]
    public void GivenAMoqMockOfWhoseReturnsACannedResultWithExitCodeAndStandardOutput(
        string className, string methodName, int exitCode, string standardOutput)
    {
        _ = className;
        _ = methodName;
        var mock = new Mock<CommandRunner>();
        var cannedResult = new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput, StandardError = "" };
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cannedResult);
        _mock = mock;
    }

    [When("the caller under test runs a command through the mock")]
    public void WhenTheCallerUnderTestRunsACommandThroughTheMock()
    {
        var request = new CommandRequest { Program = "unused" };
        _result = _mock!.Object.RunAsync(request).GetAwaiter().GetResult();
    }

    [Then("the caller sees the canned result with standard output {string}")]
    public void ThenTheCallerSeesTheCannedResultWithStandardOutput(string expected) =>
        Assert.Equal(expected, _result!.StandardOutput);

    [Then("no program is started")]
    public void ThenNoProgramIsStarted() =>
        _mock!.Verify(
            runner => runner.RunAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

    [AfterScenario]
    public void RestoreEnvironmentVariables()
    {
        foreach (var name in _envVarsToRestore)
        {
            Environment.SetEnvironmentVariable(name, _originalEnvironmentValues[name]);
        }
    }
}
