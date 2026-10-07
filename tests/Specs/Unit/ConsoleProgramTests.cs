using Core;
using Xunit;

namespace Specs.Unit;

public sealed class ConsoleProgramTests : IDisposable
{
    private const string Repo = "--repo sbiondolillo/factory-scratch";
    private readonly ConsoleHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Task<int> StartAsync(string arguments) =>
        _h.RunAsync(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), TestContext.Current.CancellationToken);

    private async Task<List<string>> RefusedAsync(string arguments)
    {
        Assert.Equal(1, await StartAsync(arguments));
        Assert.Empty(_h.Requests);
        Assert.Empty(_h.Commands);
        return [.. ConsoleHarness.Lines(_h.Error)];
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
    public async Task TwoFaultsGiveOneLineForEach()
    {
        Assert.Equal(2, (await RefusedAsync("run --repo ab --issue 0")).Count);
    }

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
        Assert.Contains(name, Assert.Single(ConsoleHarness.Lines(_h.Error)));
        Assert.Empty(_h.Requests);
        Assert.Empty(_h.Commands);
    }

    [Fact]
    public async Task BothVariablesAbsentNameTheTokenFirst()
    {
        _h.Environment.Clear();
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Contains("FACTORY_GITHUB_TOKEN", Assert.Single(ConsoleHarness.Lines(_h.Error)));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task AReadOfTheRepoThatGitHubRefusesGivesTheStatusCodeAndNoClone(int status)
    {
        _h.RepoStatus = status;
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(ConsoleHarness.Lines(_h.Error)));
        Assert.DoesNotContain(_h.Commands, c => c.Command is "git");
    }

    [Theory]
    [InlineData("/pulls", 422)]
    [InlineData("/check-runs", 500)]
    public async Task ALaterRefusalGivesTheStatusCodeAndRemovesTheWorkTree(string suffix, int status)
    {
        _h.Refusal = (suffix, status);
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1 --timeout 0"));
        string[] others = [.. ConsoleHarness.Lines(_h.Error).Where(l => !l.StartsWith("agent: ", StringComparison.Ordinal) && !l.StartsWith("command: ", StringComparison.Ordinal))];
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(others));
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task ARunThatGivesNoPullRequestExits1WithNoWatchAndNoResultLine()
    {
        _h.Lacks.Add("Solution.slnx");
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.DoesNotContain("checks:", _h.Out.ToString(), StringComparison.Ordinal);
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task AHeadThatStaysOldStartsTheWatchOnItOnlyAfter10Seconds()
    {
        _h.HeadSha = _ => "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2";
        _h.Reviews = ConsoleHarness.ReviewsJson("copilot-pull-request-reviewer", "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2");
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1"));
        TimedRequest read = _h.Requests.First(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.True(read.At - _h.PushTimes[0] >= TimeSpan.FromSeconds(10));
        Assert.True(read.At - _h.PushTimes[0] < TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task AHeadThatChangesWithin10SecondsStartsTheWatchOnTheNewCommitAtOnce()
    {
        _h.HeadSha = read => read < 4 ? "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2" : ConsoleHarness.DefaultSha;
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1"));
        TimedRequest read = _h.Requests.First(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.Contains(ConsoleHarness.DefaultSha, read.Path, StringComparison.Ordinal);
        Assert.True(read.At - _h.PushTimes[0] < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ARedWatchWithNoThreadExits1WithNoFeedbackRun()
    {
        _h.CheckRuns = _ => ConsoleHarness.RunsJson("build", "completed", "failure");
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Contains("checks: red, unresolved threads: 0", ConsoleHarness.Lines(_h.Out));
        Assert.Single(_h.CloneDirectories);
    }

    [Fact]
    public async Task ADisputedThreadGetsOneFeedbackRunAndTheSecondWatchIsTheResultLine()
    {
        _h.Threads = "always";
        _h.Disputed = true;
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal(2, _h.CloneDirectories.Count);
        Assert.Single(ConsoleHarness.Lines(_h.Out), l => l.StartsWith("checks:", StringComparison.Ordinal));
        Assert.Contains("checks: green, unresolved threads: 1", ConsoleHarness.Lines(_h.Out));
    }

    [Fact]
    public async Task AFeedbackRunReachedByARunHasItsWorkTreeRemoved()
    {
        _h.Threads = "until-resolved";
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal(2, _h.CloneDirectories.Count);
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task ARedWatchWithOneThreadRunsFeedbackOnceAndWritesTheSecondWatch()
    {
        _h.CheckRuns = _ => ConsoleHarness.RunsJson("build", "completed", "failure");
        _h.Threads = "always";
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal(2, _h.CloneDirectories.Count);
        Assert.Equal("checks: red, unresolved threads: 1", Assert.Single(ConsoleHarness.Lines(_h.Out), l => l.StartsWith("checks:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AFeedbackWithTheBudgetSpentPostsOneCommentAndExits1()
    {
        _h.Threads = "always";
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            _h.RedBuilds.Add(build);
        }

        Assert.Equal(1, await StartAsync($"feedback {Repo} --pr 10"));
        Assert.Single(_h.Posts("/issues/10/comments"));
    }

    private const string CleanupBody = "Tidy.\n\n## Scope\n\nsrc/\n\n## Check\n\n```\ntrue\n```\n";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AFailedCheckWritesItsExitCode(int code)
    {
        _h.Labels.Add("cleanup");
        _h.IssueBody = CleanupBody;
        _h.CheckResults[0] = (code, "", "");
        await StartAsync($"run {Repo} --issue 1 --timeout 0");
        Assert.Contains($"command: check exited {code}", ConsoleHarness.Lines(_h.Error));
    }

    [Fact]
    public async Task AFailedWorktreeAddWritesNoCheckLine()
    {
        _h.Labels.Add("cleanup");
        _h.IssueBody = CleanupBody;
        _h.WorktreeAddExit = 128;
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1 --timeout 0"));
        Assert.DoesNotContain(ConsoleHarness.Lines(_h.Error), l => l.StartsWith("command: check", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheFeedbackVerbWritesNoAgentAndNoCommandLines()
    {
        _h.Threads = "always";
        _h.RedBuilds.Add(1);
        await StartAsync($"feedback {Repo} --pr 10 --timeout 0");
        Assert.NotEmpty(_h.Prompts);
        Assert.DoesNotContain(ConsoleHarness.Lines(_h.Error), l => l.StartsWith("agent: ", StringComparison.Ordinal) || l.StartsWith("command: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFeedbackWithATimedOutWatchWritesRedAndExits1()
    {
        _h.CheckRuns = _ => """{"check_runs":[]}""";
        Assert.Equal(1, await StartAsync($"feedback {Repo} --pr 10 --timeout 0"));
        Assert.Contains("checks: red, unresolved threads: 0", ConsoleHarness.Lines(_h.Out));
    }

    [Fact]
    public async Task AFeedbackWithNoThreadClonesNothingAndExits0()
    {
        Assert.Equal(0, await StartAsync($"feedback {Repo} --pr 10"));
        Assert.Empty(_h.CloneDirectories);
        Assert.Contains("checks: green, unresolved threads: 0", ConsoleHarness.Lines(_h.Out));
    }

    [Fact]
    public async Task APassEnvGivenTwiceKeepsItsOrder()
    {
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1 --pass-env B_KEY --pass-env A_KEY"));
        CommandSpec spec = Assert.Single(_h.Commands, c => c.Arguments.Contains("--project") && c.Arguments.Contains("tests/Integration"));
        List<string> arguments = [.. spec.Arguments];
        Assert.Equal(["B_KEY", "A_KEY"], arguments[..arguments.IndexOf(ConsoleHarness.ContainerId)].Where(a => a is not ("exec" or "--env")));
    }

    [Fact]
    public async Task ATimeoutAbsentWaitsAnHourBeforeTheWatchGivesUp()
    {
        _h.CheckRuns = _ => """{"check_runs":[]}""";
        Assert.Equal(1, await StartAsync($"feedback {Repo} --pr 10"));
        Assert.InRange(_h.Time.Now, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(61));
    }

    private IEnumerable<TimedRequest> ReviewRequests =>
        _h.Requests.Where(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));

    [Fact]
    public async Task AReviewOfTheHeadCommitByTheReviewerSendsNoRequest()
    {
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1"));
        Assert.Empty(ReviewRequests);
    }

    [Fact]
    public async Task AReviewOfAnEarlierCommitSendsOneRequest()
    {
        _h.Reviews = ConsoleHarness.ReviewsJson("copilot-pull-request-reviewer", "9e107d9d372bb6826bd81d3542a419d6f0a3c1b2");
        await StartAsync($"run {Repo} --issue 1 --timeout 0");
        Assert.Single(ReviewRequests);
    }

    [Fact]
    public async Task AReviewOfTheHeadCommitByAnotherLoginSendsOneRequest()
    {
        _h.Reviews = ConsoleHarness.ReviewsJson("someone");
        await StartAsync($"run {Repo} --issue 1 --timeout 0");
        Assert.Single(ReviewRequests);
    }

    [Fact]
    public async Task AFailedReviewRequestGivesTheMessageAndTheExitCode1AndNoCheckRunRead()
    {
        _h.Reviews = ConsoleHarness.NoReviewsJson();
        _h.ReviewRequestError = "not available";
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Contains(ConsoleHarness.Lines(_h.Error), l => l.Contains("not available", StringComparison.Ordinal));
        Assert.DoesNotContain(_h.Requests, r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFeedbackWatchWithNoReviewSendsTheRequestBeforeTheCheckRuns()
    {
        _h.Reviews = ConsoleHarness.NoReviewsJson();
        _h.Threads = "none";
        await StartAsync($"feedback {Repo} --pr 10 --timeout 0");
        int request = _h.Requests.FindIndex(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal));
        Assert.True(request >= 0);
        Assert.True(request < _h.Requests.FindIndex(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheSecondWatchAfterAFeedbackPassRequestsTheReviewAgain()
    {
        _h.Reviews = ConsoleHarness.NoReviewsJson();
        _h.Threads = "always";
        await StartAsync($"run {Repo} --issue 1 --timeout 0");
        Assert.Equal(2, ReviewRequests.Count());
        Assert.Equal(2, _h.Requests.Count(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal)));
    }

    private int FeedbackRuns => _h.Commands.Count(c => c.Command is "git" && c.Arguments[0] is "clone") - 1;

    private void HeadChangesEachRun() => _h.HeadSha = _ => $"{FeedbackRuns:x8}{ConsoleHarness.DefaultSha[8..]}";

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task ABadReviewRoundsValueIsRefusedWithOneLine(string value)
    {
        List<string> lines = await RefusedAsync($"run {Repo} --issue 1 --review-rounds {value}");
        Assert.Contains("--review-rounds", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--review-rounds 0", 0)]
    [InlineData("--review-rounds 1", 1)]
    [InlineData("", 3)]
    public async Task TheRoundBudgetBoundsTheFeedbackRuns(string option, int expected)
    {
        _h.Threads = "always";
        HeadChangesEachRun();
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1 {option}"));
        Assert.Equal(expected, FeedbackRuns);
        Assert.Contains("unresolved threads: 1", _h.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnchangedHeadStopsAfterOneFeedbackRun()
    {
        _h.Threads = "always";
        Assert.Equal(1, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal(1, FeedbackRuns);
    }

    [Fact]
    public async Task AResolvingRoundEndsTheRunWithExitCode0()
    {
        _h.Threads = "until-resolved";
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal(1, FeedbackRuns);
        Assert.Contains("checks: green, unresolved threads: 0", ConsoleHarness.Lines(_h.Out));
    }

    [Fact]
    public async Task ACleanFirstWatchStartsNoFeedbackRun()
    {
        Assert.Equal(0, await StartAsync($"run {Repo} --issue 1"));
        Assert.Equal(0, FeedbackRuns);
    }

    [Fact]
    public async Task FeedbackHasNoReviewRoundsOptionAndMakesOneRun()
    {
        Assert.Equal(0, await StartAsync("feedback --help"));
        Assert.DoesNotContain("--review-rounds", _h.Out.ToString(), StringComparison.Ordinal);
        _h.Threads = "always";
        HeadChangesEachRun();
        Assert.Equal(1, await StartAsync($"feedback {Repo} --pr 10"));
        Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
    }
}
