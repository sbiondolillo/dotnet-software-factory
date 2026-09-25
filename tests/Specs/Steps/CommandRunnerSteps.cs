using System.Text.RegularExpressions;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private static readonly Regex ArgumentSplitter = new("(?<=\")(?:, | and )(?=\")", RegexOptions.Compiled);

    private string _program = "";
    private IReadOnlyList<string> _arguments = [];
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _extraEnvironmentVariables = [];
    private readonly List<(string Name, string? OriginalValue)> _inheritedEnvironmentOverrides = [];
    private TimeSpan? _timeout;
    private CommandResult? _result;
    private Exception? _exception;

    [AfterScenario]
    public void RestoreInheritedEnvironmentVariables()
    {
        foreach (var (name, originalValue) in _inheritedEnvironmentOverrides)
        {
            Environment.SetEnvironmentVariable(name, originalValue);
        }
    }

    [Given("^the program \"(.*?)\" with no arguments$")]
    public void GivenTheProgramWithNoArguments(string program)
    {
        _program = program;
        _arguments = [];
    }

    [Given("^the program \"(.*?)\" with arguments (.*)$")]
    public void GivenTheProgramWithArguments(string program, string rawArguments)
    {
        _program = program;
        _arguments = ArgumentSplitter
            .Split(rawArguments)
            .Select(segment => segment[1..^1])
            .ToList();
    }

    [Given("the working directory {string}")]
    public void GivenTheWorkingDirectory(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("the extra environment variable {string} set to {string}")]
    public void GivenTheExtraEnvironmentVariableSetTo(string name, string value) =>
        _extraEnvironmentVariables[name] = value;

    [Given("the inherited environment variable {string} set to {string}")]
    public void GivenTheInheritedEnvironmentVariableSetTo(string name, string value)
    {
        _inheritedEnvironmentOverrides.Add((name, Environment.GetEnvironmentVariable(name)));
        Environment.SetEnvironmentVariable(name, value);
    }

    [Given("^the timeout is (.*)$")]
    public void GivenTheTimeoutIs(string rawTimeout) => _timeout = TimeSpan.Parse(rawTimeout, System.Globalization.CultureInfo.InvariantCulture);

    [When("I run the program with CommandRunner")]
    public async Task WhenIRunTheProgramWithCommandRunner()
    {
        try
        {
            _result = await CommandRunner.RunAsync(
                _program,
                _arguments,
                _workingDirectory,
                _extraEnvironmentVariables.Count == 0 ? null : _extraEnvironmentVariables,
                _timeout);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("the CommandResult exit code is {int}")]
    public void ThenTheCommandResultExitCodeIs(int expected) => Assert.Equal(expected, RequireResult().ExitCode);

    [Then("^the CommandResult standard output is \"(.*)\"$")]
    public void ThenTheCommandResultStandardOutputIs(string expected) =>
        Assert.Equal(expected, RequireResult().StandardOutput);

    [Then("the CommandResult standard error is {string}")]
    public void ThenTheCommandResultStandardErrorIs(string expected) =>
        Assert.Equal(expected, RequireResult().StandardError);

    [Then("the CommandResult standard output, trimmed, is {string}")]
    public void ThenTheCommandResultStandardOutputTrimmedIs(string expected) =>
        Assert.Equal(expected, RequireResult().StandardOutput.Trim());

    [Then("the CommandResult standard output is {int} bytes of {string}")]
    public void ThenTheCommandResultStandardOutputIsBytesOf(int count, string character) =>
        AssertBytesOf(RequireResult().StandardOutput, count, character);

    [Then("the CommandResult standard error is {int} bytes of {string}")]
    public void ThenTheCommandResultStandardErrorIsBytesOf(int count, string character) =>
        AssertBytesOf(RequireResult().StandardError, count, character);

    [Then("^RunAsync throws a TimeoutException whose message names the program \"(.*?)\" and the timeout (.*)$")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageNamesTheProgramAndTheTimeout(
        string program, string rawTimeout)
    {
        var timeoutException = Assert.IsType<TimeoutException>(RequireException());
        Assert.Contains(program, timeoutException.Message, StringComparison.Ordinal);
        var timeout = TimeSpan.Parse(rawTimeout, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(timeout.ToString("c"), timeoutException.Message, StringComparison.Ordinal);
    }

    [Then("RunAsync throws an exception whose message names the program {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageNamesTheProgram(string program) =>
        Assert.Contains(program, RequireException().Message, StringComparison.Ordinal);

    private CommandResult RequireResult() =>
        _result ?? throw new InvalidOperationException("No CommandResult was produced.");

    private Exception RequireException() =>
        _exception ?? throw new InvalidOperationException("No exception was thrown.");

    private static void AssertBytesOf(string text, int count, string character)
    {
        Assert.Equal(count, text.Length);
        Assert.All(text, c => Assert.Equal(character[0], c));
    }
}
