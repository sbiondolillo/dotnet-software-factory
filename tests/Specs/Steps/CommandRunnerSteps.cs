using System.Text;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

/// <summary>
/// The binding for specs/features/command_runner.feature. Every scenario drives the real
/// <see cref="CommandRunner"/> in process; the mocked scenario drives a Moq stand-in instead.
/// </summary>
[Binding]
public sealed class CommandRunnerSteps
{
    private readonly List<string> _arguments = [];
    private readonly Dictionary<string, string> _extraEnvironmentVariables = [];
    private readonly List<string> _inheritedEnvironmentVariables = [];

    private string _program = "";
    private string? _workingDirectory;
    private TimeSpan? _timeout;
    private CommandResult? _result;
    private Exception? _exception;

    private Mock<CommandRunner>? _mock;
    private CommandResult? _cannedResult;
    private CommandResult? _callerResult;

    [AfterScenario]
    public void ClearTheInheritedEnvironment()
    {
        foreach (var name in _inheritedEnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Given("the program {string} with arguments:")]
    public void GivenTheProgramWithArguments(string program, DataTable table)
    {
        _program = program;

        // A table's first row lands in Header, the rest in Rows.
        _arguments.Add(table.Header.Single());
        foreach (var row in table.Rows)
        {
            _arguments.Add(row[0]);
        }
    }

    [Given("the program {string}")]
    public void GivenTheProgram(string program) => _program = program;

    [Given("the working directory {string}")]
    public void GivenTheWorkingDirectory(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("the environment variable {string} is {string}")]
    public void GivenTheEnvironmentVariableIs(string name, string value) => _extraEnvironmentVariables[name] = value;

    [Given("the environment already has {string} set to {string}")]
    public void GivenTheEnvironmentAlreadyHas(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _inheritedEnvironmentVariables.Add(name);
    }

    [Given("the timeout is {int} seconds")]
    public void GivenTheTimeoutIs(int seconds) => _timeout = TimeSpan.FromSeconds(seconds);

    [Given("a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code {int}, standard output {string} and standard error {string}")]
    public void GivenAMoqMockOfCommandRunner(int exitCode, string standardOutput, string standardError)
    {
        var canned = new CommandResult(exitCode, standardOutput, standardError);
        _cannedResult = canned;
        _mock = new Mock<CommandRunner>();
        _mock
            .Setup(runner => runner.RunAsync(It.IsAny<CommandRequest>()))
            .ReturnsAsync(canned);
    }

    [When("I run the command")]
    public async Task WhenIRunTheCommand()
    {
        var request = new CommandRequest(
            _program,
            _arguments,
            _workingDirectory,
            _extraEnvironmentVariables.Count > 0 ? _extraEnvironmentVariables : null,
            _timeout);

        try
        {
            _result = await new CommandRunner().RunAsync(request);
        }
        catch (Exception exception)
        {
            _exception = exception;
        }
    }

    [When("the caller under test runs using the mock")]
    public async Task WhenTheCallerUnderTestRunsUsingTheMock()
    {
        var caller = new Caller(_mock!.Object);
        _callerResult = await caller.RunAsync();
    }

    [Then("the result exit code is {int}")]
    public void ThenTheResultExitCodeIs(int expected) => Assert.Equal(expected, _result!.ExitCode);

    [Then("the result standard output is {string}")]
    public void ThenTheResultStandardOutputIs(string expected) => Assert.Equal(WithNewlines(expected), _result!.StandardOutput);

    [Then("the result standard error is {string}")]
    public void ThenTheResultStandardErrorIs(string expected) => Assert.Equal(WithNewlines(expected), _result!.StandardError);

    [Then("the result standard output does not contain {string}")]
    public void ThenTheResultStandardOutputDoesNotContain(string fragment) => Assert.DoesNotContain(fragment, _result!.StandardOutput);

    [Then("the result standard output is {int} bytes long")]
    public void ThenTheResultStandardOutputIsBytesLong(int expected) => Assert.Equal(expected, Encoding.UTF8.GetByteCount(_result!.StandardOutput));

    [Then("the result standard error is {int} bytes long")]
    public void ThenTheResultStandardErrorIsBytesLong(int expected) => Assert.Equal(expected, Encoding.UTF8.GetByteCount(_result!.StandardError));

    [Then("running the command throws a TimeoutException whose message names {string} and {string}")]
    public void ThenRunningTheCommandThrowsATimeoutExceptionWhoseMessageNames(string first, string second)
    {
        var timeout = Assert.IsType<TimeoutException>(_exception!);
        Assert.Contains(first, timeout.Message);
        Assert.Contains(second, timeout.Message);
    }

    [Then("running the command throws an exception whose message names {string}")]
    public void ThenRunningTheCommandThrowsAnExceptionWhoseMessageNames(string fragment) => Assert.Contains(fragment, _exception!.Message);

    [Then("the caller's result matches the canned CommandResult")]
    public void ThenTheCallersResultMatchesTheCannedCommandResult() => Assert.Equal(_cannedResult, _callerResult);

    [Then("no program was started")]
    public void ThenNoProgramWasStarted() => _mock!.Verify(runner => runner.RunAsync(It.IsAny<CommandRequest>()), Times.Once);

    /// <summary>
    /// Reqnroll passes a quoted step value through without interpreting escapes, so the
    /// "\n" the feature writes for a newline arrives as the two characters backslash and n;
    /// expand that sequence before comparing it with a captured stream.
    /// </summary>
    private static string WithNewlines(string expected) => expected.Replace("\\n", "\n");

    /// <summary>The caller the mocked scenario tests: it only knows a <see cref="CommandRunner"/>.</summary>
    private sealed class Caller
    {
        private readonly CommandRunner _runner;

        public Caller(CommandRunner runner) => _runner = runner;

        public Task<CommandResult> RunAsync() => _runner.RunAsync(new CommandRequest("irrelevant", []));
    }
}
