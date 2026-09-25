using System.Globalization;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private string _program = "";
    private readonly List<string> _arguments = [];
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _extraEnvironment = [];
    private TimeSpan? _timeout;
    private CommandResult? _result;
    private Exception? _exception;

    [Given("the program {string}")]
    public void GivenTheProgram(string program) => SetProgram(program);

    [Given("the program {string} with no arguments")]
    public void GivenTheProgramWithNoArguments(string program) => SetProgram(program);

    [Given("the program {string} with arguments {string} and {string}")]
    public void GivenTheProgramWithTwoArguments(string program, string first, string second)
    {
        SetProgram(program);
        _arguments.Add(first);
        _arguments.Add(second);
    }

    [Given("the program {string} with arguments {string}, {string}, {string} and {string}")]
    public void GivenTheProgramWithFourArguments(string program, string first, string second, string third, string fourth)
    {
        SetProgram(program);
        _arguments.Add(first);
        _arguments.Add(second);
        _arguments.Add(third);
        _arguments.Add(fourth);
    }

    [Given("the working directory is {string}")]
    public void GivenTheWorkingDirectory(string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);
        _workingDirectory = workingDirectory;
    }

    [Given("the extra environment variable {string} with value {string}")]
    public void GivenTheExtraEnvironmentVariable(string name, string value) => _extraEnvironment[name] = value;

    [Given("the environment variable {string} is {string}")]
    public static void GivenTheEnvironmentVariable(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    [Given("the timeout is {int} second")]
    public void GivenTheTimeout(int seconds) => _timeout = TimeSpan.FromSeconds(seconds);

    [When("the command runs")]
    public async Task WhenTheCommandRuns()
    {
        var request = new CommandRequest(_program, _arguments, _workingDirectory, _extraEnvironment, _timeout);
        try
        {
            _result = await CommandRunner.RunAsync(request);
        }
        catch (Exception exception)
        {
            _exception = exception;
        }
    }

    [Then("the result's exit code is {int}")]
    public void ThenTheExitCodeIs(int expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result!.ExitCode);
    }

    [Then("the result's standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result!.StandardOutput.Trim());
    }

    [Then("the result's standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result!.StandardError.Trim());
    }

    [Then("the result's standard output contains {string}")]
    public void ThenTheStandardOutputContains(string expected)
    {
        Assert.NotNull(_result);
        Assert.Contains(expected, _result!.StandardOutput);
    }

    [Then("the result's standard output does not contain {string}")]
    public void ThenTheStandardOutputDoesNotContain(string expected)
    {
        Assert.NotNull(_result);
        Assert.DoesNotContain(expected, _result!.StandardOutput);
    }

    [Then("the result's standard output is {int} characters long")]
    public void ThenTheStandardOutputIsCharactersLong(int expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result!.StandardOutput.Length);
    }

    [Then("the result's standard error is {int} characters long")]
    public void ThenTheStandardErrorIsCharactersLong(int expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(expected, _result!.StandardError.Length);
    }

    [Then("no process matching {string} runs")]
    public static async Task ThenNoProcessMatchingRuns(string pattern)
    {
        for (var attempt = 0; attempt < 10 && ProcessMatchingRuns(pattern); attempt++)
        {
            await Task.Delay(250);
        }

        Assert.False(ProcessMatchingRuns(pattern), $"a process matching '{pattern}' is still running");
    }

    [Then("RunAsync throws a TimeoutException whose message contains {string} and {string}")]
    public void ThenRunAsyncThrowsATimeoutException(string first, string second)
    {
        var exception = Assert.IsType<TimeoutException>(_exception);
        Assert.Contains(first, exception.Message);
        Assert.Contains(second, exception.Message);
    }

    [Then("RunAsync throws an exception whose message contains {string}")]
    public void ThenRunAsyncThrowsAnException(string expected)
    {
        Assert.NotNull(_exception);
        Assert.Contains(expected, _exception!.Message);
    }

    private void SetProgram(string program)
    {
        _program = program;
        _arguments.Clear();
    }

    private static bool ProcessMatchingRuns(string pattern)
    {
        var excluded = SelfAndAncestors();
        foreach (var directory in Directory.EnumerateDirectories("/proc", "*"))
        {
            var name = Path.GetFileName(directory);
            if (!name.All(char.IsDigit) || excluded.Contains(name))
            {
                continue;
            }

            string commandLine;
            try
            {
                commandLine = File.ReadAllText(Path.Combine(directory, "cmdline"));
            }
            catch (IOException)
            {
                continue; // The process exited between the listing and the read.
            }

            if (commandLine.Replace('\0', ' ').Contains(pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> SelfAndAncestors()
    {
        var pids = new HashSet<string> { Environment.ProcessId.ToString(CultureInfo.InvariantCulture) };
        for (var pid = Environment.ProcessId; (pid = ParentProcessId(pid)) > 1;)
        {
            pids.Add(pid.ToString(CultureInfo.InvariantCulture));
        }

        return pids;
    }

    private static int ParentProcessId(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return int.Parse(fields[1], CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
