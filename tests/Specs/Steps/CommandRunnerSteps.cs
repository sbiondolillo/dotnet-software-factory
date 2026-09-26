using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private string _program = "";
    private readonly List<string> _arguments = new();
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _extraEnvironment = new();
    private TimeSpan? _timeout;
    private readonly List<(string Name, bool HadValue, string? OldValue)> _inheritedEnvironment = new();
    private Mock<ICommandRunner>? _mock;
    private CommandResult? _canned;
    private CommandResult? _result;
    private Exception? _exception;

    [Given("the program {string} with arguments {string} and {string}")]
    public void GivenTheProgramWithArguments(string program, string first, string second)
    {
        _program = program;
        _arguments.Clear();
        _arguments.Add(first);
        _arguments.Add(second);
    }

    [Given("the program {string} with no arguments")]
    public void GivenTheProgramWithNoArguments(string program)
    {
        _program = program;
        _arguments.Clear();
    }

    [Given("the program {string} with no arguments and the working directory {string}")]
    public void GivenTheProgramWithNoArgumentsAndTheWorkingDirectory(string program, string workingDirectory)
    {
        _program = program;
        _arguments.Clear();
        _workingDirectory = workingDirectory;
    }

    [Given("the program {string} with arguments {string} and {string} and the environment variable {string} set to {string}")]
    public void GivenTheProgramWithArgumentsAndTheEnvironmentVariable(string program, string first, string second, string name, string value)
    {
        _program = program;
        _arguments.Clear();
        _arguments.Add(first);
        _arguments.Add(second);
        _extraEnvironment[name] = value;
    }

    [Given("the program {string} with arguments {string} and {string} and a timeout of {int} second")]
    public void GivenTheProgramWithArgumentsAndATimeout(string program, string first, string second, int seconds)
    {
        _program = program;
        _arguments.Clear();
        _arguments.Add(first);
        _arguments.Add(second);
        _timeout = TimeSpan.FromSeconds(seconds);
    }

    [Given("an inherited environment variable {string} set to {string}")]
    public void GivenAnInheritedEnvironmentVariable(string name, string value)
    {
        var oldValue = Environment.GetEnvironmentVariable(name);
        _inheritedEnvironment.Add((name, oldValue is not null, oldValue));
        Environment.SetEnvironmentVariable(name, value);
    }

    [Given("a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code {int}, standard output {string} and standard error {string}")]
    public void GivenAMoqMockOfCommandRunner(int exitCode, string standardOutput, string standardError)
    {
        _canned = new CommandResult(exitCode, standardOutput, standardError);
        _mock = new Mock<ICommandRunner>();
        _mock.Setup(r => r.RunAsync(It.IsAny<CommandRequest>())).ReturnsAsync(_canned);
    }

    [When("the command runs")]
    public async Task WhenTheCommandRuns()
    {
        var request = new CommandRequest(_program, _arguments, _workingDirectory, _extraEnvironment, _timeout);
        try
        {
            _result = await new CommandRunner().RunAsync(request);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [When("the caller under test invokes the mocked RunAsync")]
    public async Task WhenTheCallerUnderTestInvokesTheMockedRunAsync()
    {
        var request = new CommandRequest(_program, _arguments, _workingDirectory, _extraEnvironment, _timeout);
        _result = await _mock!.Object.RunAsync(request);
    }

    [Then("the exit code is {int}")]
    public void ThenTheExitCodeIs(int expected) => Assert.Equal(expected, _result!.ExitCode);

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected) => Assert.Equal(expected, TrimTrailingLineFeeds(_result!.StandardOutput));

    [Then("the standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected) => Assert.Equal(expected, TrimTrailingLineFeeds(_result!.StandardError));

    [Then("the standard output is {int} bytes long")]
    public void ThenTheStandardOutputIsBytesLong(int expected) => Assert.Equal(expected, _result!.StandardOutput.Length);

    [Then("the standard error is {int} bytes long")]
    public void ThenTheStandardErrorIsBytesLong(int expected) => Assert.Equal(expected, _result!.StandardError.Length);

    [Then("the program is no longer running")]
    public async Task ThenTheProgramIsNoLongerRunning()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (ProcessProbe.IsRunning(_program, _arguments.ToArray()) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.False(ProcessProbe.IsRunning(_program, _arguments.ToArray()));
    }

    [Then("a TimeoutException is thrown whose message names the program {string} and the timeout {string}")]
    public void ThenATimeoutExceptionIsThrown(string program, string timeout)
    {
        var ex = Assert.IsType<TimeoutException>(_exception);
        Assert.Contains(program, ex.Message);
        Assert.Contains(timeout, ex.Message);
    }

    [Then("an exception is thrown whose message names the program {string}")]
    public void ThenAnExceptionIsThrown(string program)
    {
        Assert.NotNull(_exception);
        Assert.Contains(program, _exception!.Message);
    }

    [Then("no program is started")]
    public void ThenNoProgramIsStarted()
    {
        _mock!.Verify(r => r.RunAsync(It.IsAny<CommandRequest>()), Times.Once());
        Assert.Same(_canned, _result);
    }

    [AfterScenario]
    public void RestoreInheritedEnvironment()
    {
        foreach (var (name, hadValue, oldValue) in _inheritedEnvironment)
        {
            Environment.SetEnvironmentVariable(name, hadValue ? oldValue : null);
        }

        _inheritedEnvironment.Clear();
    }

    private static string TrimTrailingLineFeeds(string value)
    {
        var length = value.Length;
        while (length > 0 && value[length - 1] == '\n')
        {
            length--;
        }

        return value[..length];
    }
}
