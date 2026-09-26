using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps : IDisposable
{
    private readonly Dictionary<string, string?> _processEnvironment = [];
    private readonly List<string> _arguments = [];
    private readonly Dictionary<string, string> _extraEnvironment = [];

    private string _name = "";
    private string? _workingDirectory;
    private TimeSpan? _timeout;
    private CommandResult? _result;
    private Exception? _exception;

    private Command? _callerCommand;
    private string[] _callerArgv = [];
    private ExitCodeCaller? _caller;
    private int? _callerExitCode;

    public void Dispose()
    {
        foreach (var (name, previous) in _processEnvironment)
        {
            Environment.SetEnvironmentVariable(name, previous);
        }

        _processEnvironment.Clear();
    }

    [Given("a command {string}")]
    public void GivenACommand(string name)
    {
        _name = name;
        _arguments.Clear();
        _extraEnvironment.Clear();
        _workingDirectory = null;
        _timeout = null;
        _result = null;
        _exception = null;
    }

    [Given("the command's working directory is {string}")]
    public void GivenTheCommandWorkingDirectoryIs(string workingDirectory) => _workingDirectory = workingDirectory;

    [Given("the command's timeout is {int} second")]
    public void GivenTheCommandTimeoutIs(int seconds) => _timeout = TimeSpan.FromSeconds(seconds);

    [Given("the process environment includes {string} set to {string}")]
    public void GivenTheProcessEnvironmentIncludes(string name, string value)
    {
        _processEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    [Given("the command's arguments are:")]
    public void GivenTheCommandArgumentsAre(Table table)
    {
        foreach (var row in table.Rows)
        {
            _arguments.Add(row[0]);
        }
    }

    [Given("the command's extra environment variables are:")]
    public void GivenTheCommandExtraEnvironmentVariablesAre(Table table)
    {
        foreach (var row in table.Rows)
        {
            _extraEnvironment[row[0]] = row[1];
        }
    }

    [When("I run the command")]
    public async Task WhenIRunTheCommand()
    {
        var command = new Command(_name, _arguments, _workingDirectory, _extraEnvironment, _timeout);
        var runner = new CommandRunner();

        try
        {
            _result = await runner.RunAsync(command).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("the result's exit code is {int}")]
    public void ThenTheResultExitCodeIs(int expected) => Assert.Equal(expected, _result?.ExitCode);

    [Then("the result's standard output is {string}")]
    public void ThenTheResultStandardOutputIs(string expected) => Assert.Equal(expected, _result?.StandardOutput);

    [Then("the result's standard error is {string}")]
    public void ThenTheResultStandardErrorIs(string expected) => Assert.Equal(expected, _result?.StandardError);

    [Then("the result's standard output has length {int}")]
    public void ThenTheResultStandardOutputHasLength(int expected) => Assert.Equal(expected, _result?.StandardOutput?.Length);

    [Then("the result's standard error has length {int}")]
    public void ThenTheResultStandardErrorHasLength(int expected) => Assert.Equal(expected, _result?.StandardError?.Length);

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected) => Assert.Equal(expected, _result?.StandardOutput);

    [Then("the result's standard output trimmed of trailing whitespace is {string}")]
    public void ThenTheResultStandardOutputTrimmedOfTrailingWhitespaceIs(string expected) =>
        Assert.Equal(expected, _result?.StandardOutput?.TrimEnd());

    [Then("running the command throws a TimeoutException")]
    public void ThenRunningTheCommandThrowsATimeoutException() => Assert.IsType<TimeoutException>(_exception);

    [Then("running the command throws an exception")]
    public void ThenRunningTheCommandThrowsAnException() => Assert.NotNull(_exception);

    [Then("the exception message contains {string}")]
    public void ThenTheExceptionMessageContains(string text)
    {
        Assert.NotNull(_exception);
        Assert.Contains(text, _exception!.Message);
    }

    [Then("the program {string} is no longer running")]
    public void ThenTheProgramIsNoLongerRunning(string name)
    {
        Assert.Equal(name, _name);
        Assert.False(ProcessProbe.IsRunning([_name, .. _arguments]), $"a process with argv {_argvDescription()} is still running");
    }

    [Given("a caller that runs a command through CommandRunner and returns its exit code")]
    public void GivenACallerThatRunsACommandThroughCommandRunner()
    {
        _callerCommand = new Command("sh", ["-c", "sleep 7"]);
        _callerArgv = ["sh", "-c", "sleep 7"];
    }

    [Given("a mock of CommandRunner whose RunAsync returns a canned result with exit code {int}")]
    public void GivenAMockOfCommandRunnerWhoseRunAsyncReturnsACannedResultWithExitCode(int exitCode)
    {
        var mock = new Mock<ICommandRunner>();
        mock
            .Setup(runner => runner.RunAsync(It.IsAny<Command>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new CommandResult(exitCode, "", "")));
        _caller = new ExitCodeCaller(mock.Object, _callerCommand!);
    }

    [When("the caller runs")]
    public async Task WhenTheCallerRuns() => _callerExitCode = await _caller!.RunAsync().ConfigureAwait(false);

    [Then("the caller returns {int}")]
    public void ThenTheCallerReturns(int expected) => Assert.Equal(expected, _callerExitCode);

    [Then("no program is started")]
    public void ThenNoProgramIsStarted() => Assert.False(ProcessProbe.IsRunning(_callerArgv), $"a process with argv {_argvDescription(_callerArgv)} is running");

    private string _argvDescription(IReadOnlyList<string>? argv = null)
    {
        var entries = argv is null ? [_name, .. _arguments] : argv;
        return "[" + string.Join(", ", entries) + "]";
    }
}
