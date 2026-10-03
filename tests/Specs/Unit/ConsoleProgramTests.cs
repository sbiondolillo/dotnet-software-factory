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

    [Theory]
    [InlineData("")]
    [InlineData("build --repo a/b")]
    [InlineData("run --issue 1")]
    [InlineData("run --repo a/b")]
    [InlineData("feedback --repo a/b")]
    [InlineData("run --repo ab --issue 1")]
    [InlineData("run --repo /b --issue 1")]
    [InlineData("run --repo a/ --issue 1")]
    [InlineData("run --repo a/b/c --issue 1")]
    [InlineData("run --repo a/b --issue x")]
    [InlineData("run --repo a/b --issue 0")]
    [InlineData("feedback --repo a/b --pr x")]
    [InlineData("run --repo a/b --issue 1 --timeout x")]
    [InlineData("run --repo a/b --issue 1 --timeout -1")]
    [InlineData("run --repo a/b --issue 1 --timeout")]
    [InlineData("run --repo a/b --issue 1 --bogus 2")]
    [InlineData("run --repo a/b --pr 1")]
    public async Task AnUsageErrorWritesTheUsageAndExits1WithNoRequestAndNoProgram(string arguments)
    {
        Assert.Equal(1, await StartAsync(arguments));
        Assert.Equal(ConsoleProgram.Usage, _h.Error.ToString());
        Assert.Empty(_h.Requests);
        Assert.Empty(_h.Commands);
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
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(ConsoleHarness.Lines(_h.Error)));
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
}
