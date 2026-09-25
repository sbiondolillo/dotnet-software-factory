using System.Text;
using System.Text.RegularExpressions;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerSteps
{
    private static readonly Regex QuotedTokenRegex = new("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
    private static readonly Regex WorkingDirectoryRegex = new(
        "^ in the working directory \"(?<dir>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled);
    private static readonly Regex ExtraEnvironmentVariableRegex = new(
        "^ with the extra environment variable \"(?<name>(?:[^\"\\\\]|\\\\.)*)\" set to \"(?<value>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled);
    private static readonly Regex TimeoutRegex = new(
        "^ with a timeout of (?<seconds>\\d+) seconds",
        RegexOptions.Compiled);
    private static readonly Regex NoExtraEnvironmentVariablesRegex = new(
        "^ with no extra environment variables",
        RegexOptions.Compiled);

    private readonly CommandRunner _runner = new();
    private string? _program;
    private IReadOnlyList<string>? _arguments;
    private string? _workingDirectory;
    private Dictionary<string, string>? _extraEnv;
    private TimeSpan? _timeout;
    private CommandResult? _result;
    private Exception? _exception;

    private SampleCaller? _caller;
    private Mock<CommandRunner>? _mock;
    private CommandResult? _canned;
    private CommandResult? _actual;

    [Given(@"^I run the program ""(?<program>(?:[^""\\]|\\.)*)"" with arguments (?<rest>.*)$")]
    public void GivenIRunTheProgramWithArguments(string program, string rest)
    {
        _program = program;

        var remainder = rest;

        var workingDirectoryMatch = WorkingDirectoryRegex.Match(FindClauseSuffix(remainder, " in the working directory \""));
        var extraEnvMatch = ExtraEnvironmentVariableRegex.Match(FindClauseSuffix(remainder, " with the extra environment variable \""));
        var timeoutMatch = TimeoutRegex.Match(FindClauseSuffix(remainder, " with a timeout of "));
        var noExtraEnvMatch = NoExtraEnvironmentVariablesRegex.Match(FindClauseSuffix(remainder, " with no extra environment variables"));

        var argumentsPart = remainder;

        if (workingDirectoryMatch.Success)
        {
            var index = remainder.IndexOf(" in the working directory \"", StringComparison.Ordinal);
            argumentsPart = remainder[..index];
            _workingDirectory = workingDirectoryMatch.Groups["dir"].Value;
        }
        else if (extraEnvMatch.Success)
        {
            var index = remainder.IndexOf(" with the extra environment variable \"", StringComparison.Ordinal);
            argumentsPart = remainder[..index];
            _extraEnv = new Dictionary<string, string>
            {
                [extraEnvMatch.Groups["name"].Value] = extraEnvMatch.Groups["value"].Value,
            };
        }
        else if (noExtraEnvMatch.Success)
        {
            var index = remainder.IndexOf(" with no extra environment variables", StringComparison.Ordinal);
            argumentsPart = remainder[..index];
            _extraEnv = new Dictionary<string, string>();
        }
        else if (timeoutMatch.Success)
        {
            var index = remainder.IndexOf(" with a timeout of ", StringComparison.Ordinal);
            argumentsPart = remainder[..index];
            _timeout = TimeSpan.FromSeconds(int.Parse(timeoutMatch.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        var tokens = argumentsPart.Trim() == "(none)"
            ? Array.Empty<string>()
            : QuotedTokenRegex.Matches(argumentsPart)
                .Select(match => match.Value[1..^1])
                .ToArray();

        // The token right after "-c" is the shell's own script text: sh must see real
        // quote characters in it to parse it correctly, so unescape only that token.
        // A token used as plain data (like a positional parameter) reaches the child
        // verbatim and is left exactly as written in the feature.
        if (tokens.Length >= 2 && tokens[0] == "-c")
        {
            tokens[1] = Unescape(tokens[1]);
        }

        _arguments = tokens;
    }

    [Given("I run a program that writes 1 MB to standard output and 1 MB to standard error")]
    public void GivenIRunAProgramThatWritesOneMegabyteToEachStream()
    {
        _program = "python3";
        _arguments = new[]
        {
            "-c",
            "import sys; sys.stdout.write('X' * 1048576); sys.stderr.write('Y' * 1048576)",
        };
    }

    [Given("the inherited environment variable {string} is set to {string}")]
    public static void GivenTheInheritedEnvironmentVariableIsSetTo(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value);

    [When("the command completes")]
    public async Task WhenTheCommandCompletes() =>
        _result = await _runner.RunAsync(_program!, _arguments!, _workingDirectory, _extraEnv, _timeout);

    [When("the command runs past its timeout")]
    public async Task WhenTheCommandRunsPastItsTimeout()
    {
        try
        {
            _result = await _runner.RunAsync(_program!, _arguments!, _workingDirectory, _extraEnv, _timeout);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [When("the command fails to start")]
    public async Task WhenTheCommandFailsToStart()
    {
        try
        {
            _result = await _runner.RunAsync(_program!, _arguments!, _workingDirectory, _extraEnv, _timeout);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("the exit code is {int}")]
    public void ThenTheExitCodeIs(int expected) => Assert.Equal(expected, _result!.ExitCode);

    [Then("the standard output is {string}")]
    public void ThenTheStandardOutputIs(string expected) => Assert.Equal(expected, _result!.StandardOutput);

    [Then("the standard error is {string}")]
    public void ThenTheStandardErrorIs(string expected) => Assert.Equal(expected, _result!.StandardError);

    [Then("the standard output is {int} bytes long")]
    public void ThenTheStandardOutputIsBytesLong(int expected) =>
        Assert.Equal(expected, Encoding.UTF8.GetByteCount(_result!.StandardOutput));

    [Then("the standard error is {int} bytes long")]
    public void ThenTheStandardErrorIsBytesLong(int expected) =>
        Assert.Equal(expected, Encoding.UTF8.GetByteCount(_result!.StandardError));

    [Then("RunAsync throws a TimeoutException")]
    public void ThenRunAsyncThrowsATimeoutException() => Assert.IsType<TimeoutException>(_exception);

    [Then("RunAsync throws an exception")]
    public void ThenRunAsyncThrowsAnException() => Assert.NotNull(_exception);

    [Then("the exception message names the program {string}")]
    public void ThenTheExceptionMessageNamesTheProgram(string expected) => Assert.Contains(expected, _exception!.Message);

    [Then("the exception message names the timeout {string}")]
    public void ThenTheExceptionMessageNamesTheTimeout(string expected) => Assert.Contains(expected, _exception!.Message);

    [Given("a caller that depends on CommandRunner")]
    public void GivenACallerThatDependsOnCommandRunner() => _caller = new SampleCaller();

    [Given("a Moq mock of CommandRunner whose RunAsync returns a canned CommandResult with exit code {int}, standard output {string} and standard error {string}")]
    public void GivenAMoqMockOfCommandRunner(int exitCode, string standardOutput, string standardError)
    {
        _canned = new CommandResult(exitCode, standardOutput, standardError);
        _mock = new Mock<CommandRunner>();
        _mock
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_canned);
    }

    [When("the caller runs using the mocked CommandRunner")]
    public async Task WhenTheCallerRunsUsingTheMockedCommandRunner() => _actual = await _caller!.RunAsync(_mock!.Object);

    [Then("the caller receives the canned CommandResult")]
    public void ThenTheCallerReceivesTheCannedCommandResult() => Assert.Equal(_canned, _actual);

    [Then("no program starts")]
    public void ThenNoProgramStarts() =>
        _mock!.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once());

    private static string FindClauseSuffix(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? string.Empty : text[index..];
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i]);
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

    private sealed class SampleCaller
    {
        private readonly string[] _arguments = { "-c", "true" };

        public Task<CommandResult> RunAsync(CommandRunner runner) => runner.RunAsync("sh", _arguments);
    }
}
