using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The console program: the parse, the help, the environment checks, a refused read of GitHub, the options it hands a
/// settle, and the exit code of each <see cref="SettleEnd"/>. The lifecycle of a pull request is in <see cref="SettleTests"/>.</summary>
public sealed class ConsoleProgramTests : IDisposable
{
    private const string Repo = "--repo sbiondolillo/factory-scratch";
    private readonly ConsoleHarness _h = new();
    private readonly SettleHarness _run = new();

    public void Dispose() => _run.Dispose();

    private Task<int> StartAsync(string arguments) => _h.RunAsync(arguments, TestContext.Current.CancellationToken);

    private Task<int> RunAsync(string arguments) =>
        _run.RunAsync(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), TestContext.Current.CancellationToken);

    private async Task<List<string>> RefusedAsync(string arguments)
    {
        Assert.Equal(1, await StartAsync(arguments));
        Assert.Empty(_h.Requests);
        Assert.Empty(_h.Commands);
        return [.. SettleHarness.Lines(_h.Error)];
    }

    [Fact]
    public async Task NoArgumentsGiveTheAbsentVerbLineAndTheProgramHelp()
    {
        Assert.Equal(["Required command was not provided."], await RefusedAsync(""));
        Assert.Contains("run", _h.Out.ToString(), StringComparison.Ordinal);
        Assert.Contains("feedback", _h.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownVerbGivesOneLineForEachFault()
    {
        Assert.Equal(
            [
                "Required command was not provided.",
                "Unrecognized command or argument 'build'.",
                "Unrecognized command or argument '--repo'.",
                "Unrecognized command or argument 'a/b'.",
            ],
            await RefusedAsync("build --repo a/b"));
    }

    [Theory]
    [InlineData("run --issue 1", "Option '--repo' is required.", "--issue <issue> (REQUIRED)")]
    [InlineData("run --repo a/b", "Option '--issue' is required.", "--repo <repo> (REQUIRED)")]
    [InlineData("feedback --repo a/b", "Option '--pr' is required.", "--pr <pr> (REQUIRED)")]
    public async Task AnAbsentRequiredOptionGivesTheParsersLineAndTheHelpOfTheVerb(string arguments, string line, string help)
    {
        Assert.Equal([line], await RefusedAsync(arguments));
        Assert.Contains(help, _h.Out.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run --repo ab --issue 1", "--repo")]
    [InlineData("run --repo /b --issue 1", "--repo")]
    [InlineData("run --repo a/ --issue 1", "--repo")]
    [InlineData("run --repo a/b/c --issue 1", "--repo")]
    [InlineData("run --repo a/b --issue 0", "--issue")]
    [InlineData("run --repo a/b --issue -1", "--issue")]
    [InlineData("feedback --repo a/b --pr 0", "--pr")]
    [InlineData("run --repo a/b --issue 1 --timeout -1", "--timeout")]
    public async Task AFaultOfTheProgramGivesOneLineNamingTheOption(string arguments, string option)
    {
        string line = Assert.Single(await RefusedAsync(arguments));
        Assert.Contains(option, line, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrecognized", line, StringComparison.Ordinal);
        Assert.Contains("--timeout", _h.Out.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run --repo a/b --issue x", "Cannot parse argument 'x' for option '--issue' as expected type 'System.Int32'.")]
    [InlineData("feedback --repo a/b --pr x", "Cannot parse argument 'x' for option '--pr' as expected type 'System.Int32'.")]
    [InlineData("run --repo a/b --issue 1 --timeout x", "Cannot parse argument 'x' for option '--timeout' as expected type 'System.Int32'.")]
    [InlineData("run --repo a/b --issue 1 --timeout", "Required argument missing for option: '--timeout'.")]
    public async Task AValueTheParserRefusesGivesItsOneLine(string arguments, string line) =>
        Assert.Equal([line], await RefusedAsync(arguments));

    [Fact]
    public async Task ABogusOptionWithAValueGivesTwoUnrecognizedLinesInOrder()
    {
        Assert.Equal(
            ["Unrecognized command or argument '--bogus'.", "Unrecognized command or argument '2'."],
            await RefusedAsync("run --repo a/b --issue 1 --bogus 2"));
    }

    [Theory]
    [InlineData("run --repo a/b --issue 1 --bogus 2", "--bogus")]
    [InlineData("run --repo a/b --pr 1", "--pr")]
    [InlineData("feedback --repo a/b --issue 1", "--issue")]
    public async Task AnUnknownOptionGivesAnUnrecognizedLine(string arguments, string option)
    {
        List<string> lines = await RefusedAsync(arguments);
        Assert.Contains($"Unrecognized command or argument '{option}'.", lines);
    }

    [Fact]
    public async Task TwoFaultsGiveOneLineForEach() =>
        Assert.Equal(2, (await RefusedAsync("run --repo ab --issue 0")).Count);

    [Theory]
    [InlineData("--help", new[] { "run", "feedback" })]
    [InlineData("run --help", new[] { "--repo <repo> (REQUIRED)", "--issue <issue> (REQUIRED)", "--pass-env <pass-env>", "[default: 60]" })]
    [InlineData("feedback --help", new[] { "--pr <pr> (REQUIRED)", "[default: 60]" })]
    public async Task HelpExits0WithNoErrorAndNeedsNoVariable(string arguments, string[] holds)
    {
        _h.Environment.Clear();
        Assert.Equal(0, await StartAsync(arguments));
        Assert.Empty(_h.Error.ToString());
        Assert.Empty(_h.Requests);
        Assert.Empty(_h.Commands);
        foreach (string text in holds)
        {
            Assert.Contains(text, _h.Out.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("FACTORY_GITHUB_TOKEN")]
    [InlineData("ANTHROPIC_API_KEY")]
    public async Task AnAbsentVariableOrAnEmptyOneGivesOneLineNamingItAndExits1(string name)
    {
        _h.Environment[name] = "";
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Contains(name, Assert.Single(SettleHarness.Lines(_h.Error)), StringComparison.Ordinal);
        Assert.Empty(_h.Requests);
        Assert.Empty(_h.Commands);
    }

    [Fact]
    public async Task BothVariablesAbsentNameTheTokenFirst()
    {
        _h.Environment.Clear();
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Contains("FACTORY_GITHUB_TOKEN", Assert.Single(SettleHarness.Lines(_h.Error)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task AReadOfTheRepoThatGitHubRefusesGivesTheStatusCodeAndNoClone(int status)
    {
        _h.Status = status;
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal("/repos/sbiondolillo/factory-scratch", Assert.Single(_h.Requests));
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(SettleHarness.Lines(_h.Error)), StringComparison.Ordinal);
        Assert.DoesNotContain(_h.Commands, c => c.Command is "git");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task ABadReviewRoundsValueIsRefusedWithOneLine(string value)
    {
        List<string> lines = await RefusedAsync($"run {Repo} --issue 1 --review-rounds {value}");
        Assert.Contains("--review-rounds", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASettledRunExits0()
    {
        Assert.Equal(0, await RunAsync($"run {Repo} --issue 1"));
        Assert.Contains("checks: green, unresolved threads: 0", SettleHarness.Lines(_run.Out));
    }

    [Fact]
    public async Task AnUnsettledRunExits1()
    {
        _run.CheckRuns = _ => SettleHarness.RunsJson("build", "completed", "failure");
        Assert.Equal(1, await RunAsync($"run {Repo} --issue 1"));
        Assert.Contains("checks: red, unresolved threads: 0", SettleHarness.Lines(_run.Out));
    }

    [Fact]
    public async Task ADeclinedRunExits0()
    {
        _run.Labels.AddRange(["cleanup", "refactor"]);
        Assert.Equal(0, await RunAsync($"run {Repo} --issue 1"));
        Assert.Empty(_run.CloneDirectories);
    }

    [Fact]
    public async Task AStoppedRunExits1()
    {
        _run.Lacks.Add("Solution.slnx");
        Assert.Equal(1, await RunAsync($"run {Repo} --issue 1"));
        Assert.DoesNotContain("checks:", _run.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALaterRefusalGivesTheStatusCodeAndExits1()
    {
        _run.Refusal = ("/pulls", 422);
        Assert.Equal(1, await RunAsync($"run {Repo} --issue 1 --timeout 0"));
        string[] others = [.. SettleHarness.Lines(_run.Error).Where(l => !l.StartsWith("agent: ", StringComparison.Ordinal) && !l.StartsWith("command: ", StringComparison.Ordinal))];
        Assert.Contains("422", Assert.Single(others), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedReviewRequestGivesTheMessageAndTheExitCode1()
    {
        _run.Reviews = SettleHarness.NoReviewsJson();
        _run.ReviewRequestError = "not available";
        Assert.Equal(1, await RunAsync($"run {Repo} --issue 1 --copilot-review on"));
        Assert.Contains(SettleHarness.Lines(_run.Error), l => l.Contains("not available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APassEnvGivenTwiceKeepsItsOrder()
    {
        Assert.Equal(0, await RunAsync($"run {Repo} --issue 1 --pass-env B_KEY --pass-env A_KEY"));
        CommandSpec spec = Assert.Single(_run.Commands, c => c.Arguments.Contains("--project") && c.Arguments.Contains("tests/Integration"));
        List<string> arguments = [.. spec.Arguments];
        Assert.Equal(["B_KEY", "A_KEY"], arguments[..arguments.IndexOf(GateSandbox.ContainerId)].Where(a => a is not ("exec" or "--env")));
    }

    [Fact]
    public async Task ATimeoutAbsentWaitsAnHourBeforeTheWatchGivesUp()
    {
        _run.CheckRuns = _ => """{"check_runs":[]}""";
        Assert.Equal(1, await RunAsync($"feedback {Repo} --pr 10"));
        Assert.InRange(_run.Time.Now, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(61));
    }

    [Fact]
    public async Task AReviewRoundsAbsentGivesThreeFeedbackRuns()
    {
        _run.Threads = "always";
        Assert.Equal(1, await RunAsync($"run {Repo} --issue 1"));
        Assert.Equal(4, _run.CloneDirectories.Count);
    }

    [Fact]
    public async Task FeedbackHasNoReviewRoundsOptionAndMakesOneRun()
    {
        Assert.Equal(0, await StartAsync("feedback --help"));
        Assert.DoesNotContain("--review-rounds", _h.Out.ToString(), StringComparison.Ordinal);
        _run.Threads = "always";
        Assert.Equal(1, await RunAsync($"feedback {Repo} --pr 10"));
        Assert.Single(_run.Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
    }
}
