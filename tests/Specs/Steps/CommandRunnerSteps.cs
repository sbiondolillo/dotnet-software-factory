using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private readonly CommandRunner _runner = new();
    private List<string> _arguments = [];
    private string _workingDirectory = "";
    private Dictionary<string, string> _extraEnvironment = new();
    private TimeSpan _timeout;
    private bool _hasTimeout;
    private CommandResult? _result;
    private Exception? _failure;

    [Given("the inherited environment variable {string} is {string}")]
    public static void GivenTheInheritedEnvironmentVariableIs(string name, string value)
        => Environment.SetEnvironmentVariable(name, value);

    [When("I run {string}")]
    public Task WhenIRun(string program) => Run(program);

    [When("I run {string} with the arguments {string} and {string}")]
    public Task WhenIRunWithTwoArguments(string program, string first, string second)
    {
        _arguments = [first, second];
        return Run(program);
    }

    [When("I run {string} with the arguments {string} and {string} and {string} and {string}")]
    public Task WhenIRunWithFourArguments(string program, string first, string second, string third, string fourth)
    {
        _arguments = [first, second, third, fourth];
        return Run(program);
    }

    [When("I run {string} in the working directory {string}")]
    public Task WhenIRunInTheWorkingDirectory(string program, string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);
        _workingDirectory = workingDirectory;
        return Run(program);
    }

    [When("I run {string} with the arguments {string} and {string} and the extra environment variable {string} is {string}")]
    public Task WhenIRunWithAnExtraEnvironmentVariable(string program, string first, string second, string name, string value)
    {
        _arguments = [first, second];
        _extraEnvironment[name] = value;
        return Run(program);
    }

    [When("I run {string} with the arguments {string} and {string} with a timeout of {int} milliseconds")]
    public Task WhenIRunWithATimeout(string program, string first, string second, int milliseconds)
    {
        _arguments = [first, second];
        _timeout = TimeSpan.FromMilliseconds(milliseconds);
        _hasTimeout = true;
        return Run(program);
    }

    private async Task Run(string program)
    {
        try
        {
            _result = await _runner.RunAsync(
                program,
                _arguments,
                _workingDirectory.Length > 0 ? _workingDirectory : null,
                _extraEnvironment.Count > 0 ? _extraEnvironment : null,
                _hasTimeout ? _timeout : null);
        }
        catch (Exception e)
        {
            _failure = e;
        }
    }

    [Then("the CommandResult exit code is {int}")]
    public void ThenTheCommandResultExitCodeIs(int expected)
    {
        var result = _result;
        Assert.NotNull(result);
        Assert.Equal(expected, result.ExitCode);
    }

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected)
    {
        var result = _result;
        Assert.NotNull(result);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Then("the standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected)
    {
        var result = _result;
        Assert.NotNull(result);
        Assert.Equal(expected, result.StandardError);
    }

    [Then("the standard output is empty")]
    public void ThenTheStandardOutputIsEmpty()
    {
        var result = _result;
        Assert.NotNull(result);
        Assert.Equal("", result.StandardOutput);
    }

    [Then("the standard output holds {int} characters")]
    public void ThenTheStandardOutputHolds(int count)
    {
        var result = _result;
        Assert.NotNull(result);
        Assert.Equal(count, result.StandardOutput.Length);
    }

    [Then("the standard error holds {int} characters")]
    public void ThenTheStandardErrorHolds(int count)
    {
        var result = _result;
        Assert.NotNull(result);
        Assert.Equal(count, result.StandardError.Length);
    }

    [Then("RunAsync throws a TimeoutException whose message names {string} and {string}")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageNames(string program, string timeoutText)
    {
        var failure = _failure;
        Assert.NotNull(failure);
        var timeout = Assert.IsType<TimeoutException>(failure);
        Assert.Contains(program, timeout.Message);
        Assert.Contains(timeoutText, timeout.Message);
    }

    [Then("RunAsync throws an exception whose message names {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageNames(string program)
    {
        var failure = _failure;
        Assert.NotNull(failure);
        Assert.Contains(program, failure.Message);
    }
}
