using System.Diagnostics;
using System.Globalization;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private readonly CommandRunner _runner = new();
    private string _program = "";
    private IReadOnlyList<string> _arguments = [];
    private string? _workingDirectory;
    private Dictionary<string, string> _environment = [];
    private string _argument = "";
    private string? _sleeperPidFile;
    private CommandResult? _result;
    private Exception? _exception;

    [Given("a program that prints {string} and exits with code 3")]
    public void GivenPrintsAndExits3(string text)
    {
        _program = "sh";
        _arguments = ["-c", $"echo {text}; exit 3"];
    }

    [Given("a program that prints {string}")]
    public void GivenPrints(string text)
    {
        _program = "sh";
        _arguments = ["-c", $"echo {text}"];
    }

    [Given("a program that prints {string} to standard output and {string} to standard error")]
    public void GivenPrintsBoth(string outText, string errText)
    {
        _program = "sh";
        _arguments = ["-c", $"echo {outText}; echo {errText} >&2"];
    }

    [Given("a program that writes one megabyte to standard output and one megabyte to standard error")]
    public void GivenOneMegabyteEach()
    {
        _program = "python3";
        _arguments = ["-c", "import os; os.write(1, b\"A\"*1048576); os.write(2, b\"B\"*1048576)"];
    }

    [Given(@"the argument `(.*)`")]
    public void GivenTheArgument(string argument) => _argument = argument;

    [Given(@"the working directory is `(.*)`")]
    public void GivenTheWorkingDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        _workingDirectory = directory;
    }

    [Given(@"the extra environment variable (\S+) as ""([^""]*)""")]
    public void GivenExtraEnvironmentVariable(string name, string value) => _environment[name] = value;

    [Given(@"the inherited environment variable (\S+) as ""([^""]*)""")]
    public static void GivenInheritedEnvironmentVariable(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    [Given("a program that sleeps for ten seconds")]
    public void GivenSleepsForTenSeconds()
    {
        _sleeperPidFile = Path.Combine(Path.GetTempPath(), $"command-runner-sleeper-{Guid.NewGuid():N}.pid");
        _program = "sh";
        _arguments = ["-c", $"sleep 10 & echo $! > {_sleeperPidFile}; wait"];
    }

    [Given("a program that is on no PATH")]
    public void GivenProgramNotOnPath() => _program = "factory-nonexistent-program-28da2742";

    [When("I run it with CommandRunner")]
    public async Task WhenIRunIt()
    {
        try
        {
            _result = await _runner.RunAsync(_program, _arguments, _workingDirectory, _environment.Count > 0 ? _environment : null);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [When("I run the program with that argument")]
    public async Task WhenIRunTheProgramWithThatArgument()
    {
        // The inner sh sees the argument as exactly one positional parameter; it prints the count, then the argument.
        _result = await _runner.RunAsync("sh", ["-c", "printf \"%d\\n\" $#; printf \"%s\\n\" \"$@\"", "sh", _argument]);
    }

    [When(@"I run `(.*)` with CommandRunner")]
    public async Task WhenIRunTheNamedCommand(string command)
    {
        // A bare command like pwd is a shell builtin, so the binding runs it through sh in the given working directory.
        _result = await _runner.RunAsync("sh", ["-c", command], _workingDirectory);
    }

    [When(@"I run the program that prints its (\S+) environment variable")]
    public async Task WhenIPrintTheNamedVariable(string name)
    {
        _result = await _runner.RunAsync("sh", ["-c", $"echo ${name}"], null, _environment.Count > 0 ? _environment : null);
    }

    [When("I run it with CommandRunner with a timeout of one second")]
    public async Task WhenIRunItWithOneSecondTimeout()
    {
        try
        {
            _result = await _runner.RunAsync(_program, _arguments, null, null, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("the CommandResult exit code is {int}")]
    public void ThenExitCodeIs(int expected) => Assert.Equal(expected, _result!.ExitCode);

    [Then("the CommandResult standard output is {string}")]
    public void ThenStandardOutputIs(string expected) => Assert.Equal(expected, _result!.StandardOutput.TrimEnd('\n'));

    [Then("the CommandResult standard error is {string}")]
    public void ThenStandardErrorIs(string expected) => Assert.Equal(expected, _result!.StandardError.TrimEnd('\n'));

    [Then("the CommandResult standard output is {int} bytes and every byte is {string}")]
    public void ThenStandardOutputBytes(int count, string character)
    {
        Assert.Equal(count, _result!.StandardOutput.Length);
        Assert.True(_result.StandardOutput.All(c => c == character[0]));
    }

    [Then("the CommandResult standard error is {int} bytes and every byte is {string}")]
    public void ThenStandardErrorBytes(int count, string character)
    {
        Assert.Equal(count, _result!.StandardError.Length);
        Assert.True(_result.StandardError.All(c => c == character[0]));
    }

    [Then("the program saw exactly one argument")]
    public void ThenProgramSawExactlyOneArgument() => Assert.StartsWith("1\n", _result!.StandardOutput);

    [Then(@"the program saw the argument `(.*)` character for character")]
    public void ThenProgramSawArgument(string expected) => Assert.Equal($"1\n{expected}", _result!.StandardOutput.TrimEnd('\n'));

    [Then(@"the CommandResult standard output is `(.*)`")]
    public void ThenStandardOutputIsBackticked(string expected) => Assert.Equal(expected, _result!.StandardOutput.TrimEnd('\n'));

    [Then("RunAsync throws a TimeoutException")]
    public void ThenRunAsyncThrowsTimeoutException()
    {
        Assert.NotNull(_exception);
        Assert.IsType<TimeoutException>(_exception);
    }

    [Then(@"the exception message names the program `(.*)`")]
    public void ThenMessageNamesProgram(string program) => Assert.Contains(program, _exception!.Message);

    [Then(@"the exception message names the timeout `(.*)`")]
    public void ThenMessageNamesTimeout(string timeout) => Assert.Contains(timeout, _exception!.Message);

    [Then("the program is stopped")]
    public void ThenProgramIsStopped()
    {
        int pid = int.Parse(File.ReadAllText(_sleeperPidFile!).Trim(), CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.WaitForExit(0));
        }
        catch (ArgumentException)
        {
            // The process is not running: it was stopped.
        }
    }

    [Then("RunAsync throws an exception")]
    public void ThenRunAsyncThrows() => Assert.NotNull(_exception);

    [Then("the exception message names the program")]
    public void ThenMessageNamesProgramBare() => Assert.Contains(_program, _exception!.Message);
}
