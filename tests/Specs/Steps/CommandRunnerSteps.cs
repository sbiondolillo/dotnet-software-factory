using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

/// <summary>
/// The steps for command-runner.feature, driving <see cref="CommandRunner"/> in process.
/// Reqnroll makes one instance of this class per scenario; the fields carry values between steps.
/// </summary>
[Binding]
public sealed class CommandRunnerSteps
{
    private string _program = "";
    private List<string> _arguments = [];
    private string? _workingDirectory;
    private Dictionary<string, string> _inheritedEnvironmentVariables = [];
    private Dictionary<string, string> _extraEnvironmentVariables = [];
    private bool _hasExtraEnvironmentVariables;
    private CommandResult? _result;
    private Exception? _exception;
    private HashSet<int> _sleepPidsBeforeRun = [];

    [Given("the program is {string}")]
    public void GivenTheProgramIs(string program) => _program = program;

    [Given(@"its arguments are (.+)")]
    public void GivenItsArgumentsAre(string stepText) => _arguments = ParseQuotedSpans(stepText);

    [Given("the working directory is {string}")]
    public void GivenTheWorkingDirectoryIs(string path)
    {
        _workingDirectory = path;
        Directory.CreateDirectory(path);
    }

    [Given("the inherited environment variable {string} is {string}")]
    public void GivenTheInheritedEnvironmentVariableIs(string name, string value)
    {
        _inheritedEnvironmentVariables[name] = value;
        Environment.SetEnvironmentVariable(name, value);
    }

    [Given("the extra environment variables are")]
    public void GivenTheExtraEnvironmentVariablesAre(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            _extraEnvironmentVariables[(string)row["name"]] = (string)row["value"];
        }

        _hasExtraEnvironmentVariables = true;
    }

    [When("I run the program")]
    public async Task WhenIRunTheProgram() => await RunTheProgramAsync(timeout: null);

    [When("I run the program with a timeout of {int} seconds")]
    public async Task WhenIRunTheProgramWithATimeoutOf(int seconds) =>
        await RunTheProgramAsync(TimeSpan.FromSeconds(seconds));

    [Then("RunAsync returns without throwing")]
    public void ThenRunAsyncReturnsWithoutThrowing() => Assert.Null(_exception);

    [Then("the exit code is {int}")]
    public void ThenTheExitCodeIs(int expected) => Assert.Equal(expected, Result().ExitCode);

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected) => Assert.Equal(expected, Result().StandardOutput);

    [Then("the standard output contains {string}")]
    public void ThenTheStandardOutputContains(string expected) =>
        Assert.Contains(expected, Result().StandardOutput);

    [Then("the standard output does not contain {string}")]
    public void ThenTheStandardOutputDoesNotContain(string unexpected) =>
        Assert.DoesNotContain(unexpected, Result().StandardOutput);

    [Then("the standard error contains {string}")]
    public void ThenTheStandardErrorContains(string expected) => Assert.Contains(expected, Result().StandardError);

    [Then("the standard output is {int} characters long")]
    public void ThenTheStandardOutputIsCharactersLong(int expected) =>
        Assert.Equal(expected, Result().StandardOutput.Length);

    [Then("the standard error is {int} characters long")]
    public void ThenTheStandardErrorIsCharactersLong(int expected) =>
        Assert.Equal(expected, Result().StandardError.Length);

    [Then("RunAsync throws a TimeoutException")]
    public void ThenRunAsyncThrowsATimeoutException() => Assert.IsType<TimeoutException>(_exception);

    [Then("the exception message contains {string}")]
    public void ThenTheExceptionMessageContains(string expected) => Assert.Contains(expected, ExceptionMessage());

    [Then("RunAsync throws an exception whose message contains {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageContains(string expected) =>
        Assert.Contains(expected, ExceptionMessage());

    [Then("the program is no longer running")]
    public void ThenTheProgramIsNoLongerRunning()
    {
        var newSleepPids = SleepingPids().Except(_sleepPidsBeforeRun).ToArray();
        Assert.Empty(newSleepPids);
    }

    private async Task RunTheProgramAsync(TimeSpan? timeout)
    {
        _sleepPidsBeforeRun = SleepingPids();
        var runner = new CommandRunner();
        try
        {
            _result = await runner.RunAsync(
                _program,
                _arguments,
                _workingDirectory,
                _hasExtraEnvironmentVariables ? _extraEnvironmentVariables : null,
                timeout);
        }
        catch (Exception exception)
        {
            _exception = exception;
        }
    }

    private CommandResult Result()
    {
        Assert.NotNull(_result);
        return _result!;
    }

    private string ExceptionMessage()
    {
        Assert.NotNull(_exception);
        return _exception!.Message;
    }

    private static List<string> ParseQuotedSpans(string text)
    {
        var arguments = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            if (position > 0)
            {
                const string separator = " and ";
                if (!text.AsSpan(position).StartsWith(separator, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Expected '{separator}' before position {position} in '{text}'.");
                }

                position += separator.Length;
            }

            if (position >= text.Length || text[position] is not ('"' or '\''))
            {
                throw new ArgumentException($"Expected a quoted argument at position {position} in '{text}'.");
            }

            var quote = text[position];
            var end = text.IndexOf(quote, position + 1);
            if (end < 0)
            {
                throw new ArgumentException($"Unterminated quoted argument in '{text}'.");
            }

            arguments.Add(text[(position + 1)..end]);
            position = end + 1;
        }

        return arguments;
    }

    private static HashSet<int> SleepingPids()
    {
        var pids = new HashSet<int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                if (File.ReadAllText(Path.Combine(directory, "comm")).TrimEnd() == "sleep")
                {
                    pids.Add(pid);
                }
            }
            catch (IOException)
            {
                // The process exited while it was being read.
            }
            catch (UnauthorizedAccessException)
            {
                // The process belongs to another user.
            }
        }

        return pids;
    }
}
