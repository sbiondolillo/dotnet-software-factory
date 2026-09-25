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
    private readonly Dictionary<string, string> _environment = [];
    private CommandResult? _result;
    private Exception? _error;
    private Mock<CommandRunner>? _mock;
    private CommandResult? _callerResult;

    [Given("a command {string} with arguments {string} and {string}")]
    public void GivenACommandWithTwoArguments(string program, string first, string second)
    {
        _program = program;
        _arguments.Clear();
        _arguments.Add(first);
        _arguments.Add(second);
    }

    [Given("a command {string} with arguments {string} and {string} run in the working directory {string}")]
    public void GivenACommandWithTwoArgumentsInWorkingDirectory(string program, string first, string second, string workingDirectory)
    {
        GivenACommandWithTwoArguments(program, first, second);
        _workingDirectory = workingDirectory;
    }

    [Given("a command {string} with arguments {string} and {string} with the extra environment variable {string} set to {string}")]
    public void GivenACommandWithTwoArgumentsAndEnvironment(string program, string first, string second, string name, string value)
    {
        GivenACommandWithTwoArguments(program, first, second);
        _environment[name] = value;
    }

    [Given("a command {string} with arguments {string} and {string} with no extra environment variables")]
    public void GivenACommandWithTwoArgumentsAndNoEnvironment(string program, string first, string second)
    {
        GivenACommandWithTwoArguments(program, first, second);
    }

    [Given("a command {string} with arguments {string}")]
    public void GivenACommandWithOneArgument(string program, string argument)
    {
        _program = program;
        _arguments.Clear();
        _arguments.Add(argument);
    }

    [Given("a command {string} with no arguments")]
    public void GivenACommandWithNoArguments(string program)
    {
        _program = program;
        _arguments.Clear();
    }

    [Given("the process environment variable {string} is set to {string}")]
#pragma warning disable CA1822 // A Reqnroll step must stay an instance member of the binding class.
    public void GivenTheProcessEnvironmentVariableIsSetTo(string name, string value)
        => Environment.SetEnvironmentVariable(name, value);
#pragma warning restore CA1822

    [Given("a mock of CommandRunner whose RunAsync returns a CommandResult with exit code {int}, standard output {string} and standard error {string}")]
    public void GivenAMockOfCommandRunner(int exitCode, string standardOutput, string standardError)
    {
        _mock = new Mock<CommandRunner>();
        _mock
            .Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(new CommandResult(exitCode, standardOutput, standardError));
    }

    [When("I run the command with a timeout of {int} seconds")]
    public async Task WhenIRunTheCommandWithATimeout(int seconds)
    {
        _result = null;
        _error = null;
        try
        {
            var runner = new CommandRunner();
            _result = await runner.RunAsync(_program, _arguments, _workingDirectory, _environment, TimeSpan.FromSeconds(seconds));
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    [When("a caller asks the mock to run {string} with arguments {string}")]
    public async Task WhenACallerAsksTheMockToRun(string program, string argument)
    {
        _callerResult = null;
        _error = null;
        var runner = _mock!.Object;
        try
        {
            _callerResult = await runner.RunAsync(program, new[] { argument }, null, null, TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    [Then("the command result exit code is {int}")]
    public void ThenTheCommandResultExitCodeIs(int expected)
    {
        if (_error is not null)
        {
            Assert.Fail($"RunAsync threw {_error.GetType().Name}: {_error.Message}");
        }

        Assert.Equal(expected, _result!.ExitCode);
    }

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected)
    {
        if (_error is not null)
        {
            Assert.Fail($"RunAsync threw {_error.GetType().Name}: {_error.Message}");
        }

        Assert.Equal(expected, _result!.StandardOutput);
    }

    [Then("the standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected)
    {
        if (_error is not null)
        {
            Assert.Fail($"RunAsync threw {_error.GetType().Name}: {_error.Message}");
        }

        Assert.Equal(expected, _result!.StandardError);
    }

    [Then("the standard output is {int} bytes of {string}")]
    public void ThenTheStandardOutputIsBytesOf(int count, char ch)
    {
        if (_error is not null)
        {
            Assert.Fail($"RunAsync threw {_error.GetType().Name}: {_error.Message}");
        }

        Assert.Equal(new string(ch, count), _result!.StandardOutput);
    }

    [Then("the standard error is {int} bytes of {string}")]
    public void ThenTheStandardErrorIsBytesOf(int count, char ch)
    {
        if (_error is not null)
        {
            Assert.Fail($"RunAsync threw {_error.GetType().Name}: {_error.Message}");
        }

        Assert.Equal(new string(ch, count), _result!.StandardError);
    }

    [Then("RunAsync throws a TimeoutException whose message contains {string} and {string}")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageContains(string first, string second)
    {
        var timeout = Assert.IsType<TimeoutException>(_error!);
        Assert.Contains(first, timeout.Message);
        Assert.Contains(second, timeout.Message);
    }

    [Then("RunAsync throws an exception whose message contains {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageContains(string text)
        => Assert.Contains(text, _error!.Message);

    [Then("no process running {string} with arguments {string} remains")]
#pragma warning disable CA1822 // A Reqnroll step must stay an instance member of the binding class.
    public void ThenNoProcessRunningWithArgumentsRemains(string program, string argument)
        => Assert.False(ProcessIsRunning(program, argument));

    [Then("no process named {string} is started")]
    public void ThenNoProcessNamedIsStarted(string program)
        => Assert.False(ProcessIsRunning(program, null));
#pragma warning restore CA1822

    [Then("the caller receives the exit code {int}, the standard output {string} and the standard error {string}")]
    public void ThenTheCallerReceives(int exitCode, string standardOutput, string standardError)
    {
        if (_error is not null)
        {
            Assert.Fail($"the mock threw {_error.GetType().Name}: {_error.Message}");
        }

        var received = _callerResult!;
        Assert.Equal(exitCode, received.ExitCode);
        Assert.Equal(standardOutput, received.StandardOutput);
        Assert.Equal(standardError, received.StandardError);
    }

    // Reads each /proc/[0-9]*/cmdline, splits it on NUL, and matches the basename of argv[0]
    // against the program name plus the exact remaining argv. Never a substring scan of the
    // whole cmdline: the process that runs this session carries the prompt text, which
    // contains the scenario's program names, on its own command line. An empty cmdline
    // (a zombie) matches nothing.
    private static bool ProcessIsRunning(string program, string? argument)
    {
        foreach (var entry in Directory.GetFileSystemEntries("/proc", "[0-9]*"))
        {
            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(entry, "cmdline"));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (cmdline.Length == 0)
            {
                continue;
            }

            var argv = cmdline.Split('\0');
            var name = Path.GetFileName(argv[0]);
            if (name != program)
            {
                continue;
            }

            if (argument is null)
            {
                return true;
            }

            if (argv.Length == 2 && argv[1] == argument)
            {
                return true;
            }
        }

        return false;
    }
}
