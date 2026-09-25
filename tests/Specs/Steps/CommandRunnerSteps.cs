using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private readonly CommandRunner _runner = new();

    private TimeSpan _timeout = TimeSpan.FromSeconds(60);
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _extraEnvironment = new();

    private CommandResult? _result;
    private Exception? _error;
    private string _lastScript = "";

    [Given("the timeout is {int} seconds")]
    public void GivenTheTimeoutIs(int seconds) => _timeout = TimeSpan.FromSeconds(seconds);

    [Given("the working directory is {word}")]
    public void GivenTheWorkingDirectoryIs(string directory) => _workingDirectory = directory;

    [Given("the extra environment variable {word} is {string}")]
    public void GivenTheExtraEnvironmentVariableIs(string name, string value) => _extraEnvironment[name] = value;

    [When("I run {word} with the arguments -c {string}")]
    public async Task WhenIRunWithTheScript(string program, string script) =>
        await RunAsync(program, new[] { "-c", script }).ConfigureAwait(false);

    [When("I run {word} with the arguments -c {string} and {string}")]
    public async Task WhenIRunWithTheScriptAndArgument(string program, string script, string argument) =>
        await RunAsync(program, new[] { "-c", script, argument }).ConfigureAwait(false);

    [When("I run {word} with the argument {word}")]
    public async Task WhenIRunWithTheArgument(string program, string argument) =>
        await RunAsync(program, new[] { argument }).ConfigureAwait(false);

    private async Task RunAsync(string program, string[] arguments)
    {
        _lastScript = arguments.Length > 1 ? arguments[1] : "";
        try
        {
            _result = await _runner.RunAsync(program, arguments, _workingDirectory, _extraEnvironment, _timeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            _error = exception;
        }
        catch (InvalidOperationException exception)
        {
            _error = exception;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _error = exception;
        }
        catch (IOException exception)
        {
            _error = exception;
        }
    }

    [Then("the CommandResult has exit code {int} and empty standard output and empty standard error")]
    public void ThenTheCommandResultHasExitCodeAndEmptyStreams(int expected)
    {
        AssertResult(expected);
        Assert.Empty(_result!.StandardOutput);
        Assert.Empty(_result.StandardError);
    }

    [Then("the CommandResult has exit code {int} and its standard output holds {string}")]
    public void ThenTheCommandResultHasExitCodeAndStandardOutputHolds(int expected, string text)
    {
        AssertResult(expected);
        Assert.Contains(text, _result!.StandardOutput);
    }

    [Then("the CommandResult has exit code {int} and its standard error holds {string}")]
    public void ThenTheCommandResultHasExitCodeAndStandardErrorHolds(int expected, string text)
    {
        AssertResult(expected);
        Assert.Contains(text, _result!.StandardError);
    }

    [Then("its standard output holds none of {string}")]
    public void ThenItsStandardOutputHoldsNoneOf(string text) => Assert.DoesNotContain(text, _result!.StandardOutput);

    [Then("the CommandResult has exit code {int} and its standard output holds {int} bytes, all of them the character {string}")]
    public void ThenTheCommandResultHasExitCodeAndStandardOutputHoldsAllOneCharacter(int expected, int count, string character)
    {
        AssertResult(expected);
        string output = _result!.StandardOutput;
        Assert.Equal(count, output.Length);
        Assert.All(output, c => Assert.Equal(character[0], c));
    }

    [Then("its standard error holds {int} bytes, all of them the character {string}")]
    public void ThenItsStandardErrorHoldsAllOneCharacter(int count, string character)
    {
        string error = _result!.StandardError;
        Assert.Equal(count, error.Length);
        Assert.All(error, c => Assert.Equal(character[0], c));
    }

    [Then("the CommandResult has exit code {int} and its standard output is exactly {string}")]
    public void ThenTheCommandResultHasExitCodeAndStandardOutputIsExactly(int expected, string text)
    {
        AssertResult(expected);
        Assert.Equal(text, _result!.StandardOutput);
    }

    [Then("the CommandResult has exit code {int} and its standard output holds the inherited value of {word}")]
    public void ThenTheCommandResultHasExitCodeAndStandardOutputHoldsTheInheritedValueOf(int expected, string variable)
    {
        AssertResult(expected);
        string inherited = Environment.GetEnvironmentVariable(variable) ?? "";
        Assert.Contains(inherited, _result!.StandardOutput);
    }

    [Then("RunAsync throws a TimeoutException whose message names {string} and {string}")]
    public void ThenRunAsyncThrowsATimeoutExceptionWhoseMessageNames(string first, string second)
    {
        TimeoutException timeout = Assert.IsType<TimeoutException>(_error);
        Assert.Contains(first, timeout.Message);
        Assert.Contains(second, timeout.Message);
    }

    [Then("the program is stopped")]
    public async Task ThenTheProgramIsStopped()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (ScriptRunning(_lastScript) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        Assert.False(ScriptRunning(_lastScript));
    }

    [Then("RunAsync throws an exception whose message names {string}")]
    public void ThenRunAsyncThrowsAnExceptionWhoseMessageNames(string name)
    {
        Assert.NotNull(_error);
        Assert.Contains(name, _error!.Message);
    }

    private void AssertResult(int expectedExitCode)
    {
        Assert.Null(_error);
        Assert.NotNull(_result);
        Assert.Equal(expectedExitCode, _result!.ExitCode);
    }

    // The image has no ps or pgrep, so "the program is stopped" is checked by scanning
    // every /proc/<pid>/cmdline. A process counts as running the script when one of its
    // argv entries is exactly the script (the "sh -c <script>" wrapper) or when its whole
    // argv is the script's words (its "sleep 60" child): a plain substring match would
    // also hit any process that merely mentions the text in a longer argument.
    private static bool ScriptRunning(string scriptText)
    {
        foreach (string entry in Directory.EnumerateDirectories("/proc"))
        {
            string pid = Path.GetFileName(entry);
            if (!pid.All(char.IsDigit))
            {
                continue;
            }

            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(entry, "cmdline"));
            }
            catch (IOException)
            {
                // The process exited between the listing and the read.
                continue;
            }

            string[] arguments = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (arguments.Any(argument => argument == scriptText) || string.Join(" ", arguments) == scriptText)
            {
                return true;
            }
        }

        return false;
    }
}
