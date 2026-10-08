using System.Text.Json;
using System.Text.RegularExpressions;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed partial class ConsoleProgramSteps : IDisposable
{
    private readonly ConsoleHarness _h = new();
    private readonly ConsoleState _state;
    private readonly FactoryRunSteps _factory;

    public ConsoleProgramSteps(ConsoleState state, FactoryRunSteps factory)
    {
        _state = state;
        _factory = factory;
        factory.OtherCommands = _h.Commands;
    }

    public ConsoleHarness Harness => _h;

    public void Dispose() => _h.Dispose();

    private IEnumerable<CommandSpec> Clones => _h.Commands.Where(c => c.Command is "git" && c.Arguments[0] is "clone");

    private IEnumerable<TimedRequest> CheckReads => _h.Requests.Where(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));

    private IEnumerable<TimedRequest> ReviewRequests => _h.Requests.Where(IsReviewRequest);

    private static bool IsReviewRequest(TimedRequest r) =>
        r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains("requestReviewsByLogin", StringComparison.Ordinal);

    private static bool IsHeadRead(TimedRequest r) =>
        r.Method is "GET" && r.Path.Contains("/pulls/", StringComparison.Ordinal);

    [GeneratedRegex("^factory/[0-9a-f]{8}$")]
    private static partial Regex BranchName();

    [Given("the environment holds {string} and {string}")]
    public void GivenEnvironment(string token, string key)
    {
        Assert.True(_h.Environment.ContainsKey(token));
        Assert.True(_h.Environment.ContainsKey(key));
    }

    [Given("the clone holds the four paths of the layout")]
    public void GivenLayout() => Assert.Empty(_h.Lacks);

    [Given("the clone lacks Solution.slnx")]
    public void GivenLacksSolution() => _h.Lacks.Add("Solution.slnx");

    [Given("the environment lacks {string}")]
    public void GivenEnvironmentLacks(string name) => _h.Environment.Remove(name);

    [Given("GitHub answers the read of the repo with the default branch {string}")]
    public void GivenBranch(string branch) => _h.DefaultBranch = branch;

    [Given("GitHub answers the read of the repo with the status code {int}")]
    public void GivenRepoStatus(int status) => _h.RepoStatus = status;

    [Given("docker answers image inspect with the exit code {int}")]
    public void GivenInspect(int code) => _h.InspectExit = code;

    [Given("GitHub answers each read of the head commit of the pull request 10 with {string}")]
    public void GivenHeadAlways(string sha) => _h.HeadSha = _ => sha;

    [Given("GitHub answers the first read of the head commit of the pull request 10 with {string} and each later read with {string}")]
    public void GivenHeadThen(string first, string later) => _h.HeadSha = read => read is 1 ? first : later;

    [Given("GitHub answers each later read of the check runs with the check run {string} with the status {string} and the conclusion {string}")]
    public void GivenLaterRuns(string name, string status, string conclusion)
    {
        string later = ConsoleHarness.RunsJson(name, status, conclusion);
        _h.CheckRuns = read => read is 1 && _state.FirstCheckRun is { } first
            ? ConsoleHarness.RunsJson(first.Name, first.Status, null)
            : later;
    }

    [Given("GitHub answers each read of the reviews with one review with the author login {string}, the state {string} and the commit {string}")]
    public void GivenReviews(string login, string state, string commit) =>
        _h.Reviews = ConsoleHarness.ReviewsJson(login, commit).Replace("COMMENTED", state, StringComparison.Ordinal);

    [Given("GitHub answers the review request with a GraphQL error")]
    public void GivenRequestError() => _h.ReviewRequestError = "Copilot code review is not available for this repo.";

    [Given("GitHub answers each read of the check runs with a red check")]
    public void GivenRed() => _h.CheckRuns = _ => ConsoleHarness.RunsJson("build", "completed", "failure");

    [Given("GitHub answers each read of the review threads with no thread")]
    public void GivenNoThread() => _h.Threads = "none";

    [Given("GitHub answers each read of the review threads with the thread A")]
    public void GivenThreadA() => _h.Threads = "always";

    [Given("GitHub answers each read of the review threads with the thread A until the resolve of the thread A, and with no thread after it")]
    public void GivenThreadUntilResolved() => _h.Threads = "until-resolved";

    [Given("the agent writes the disputed report")]
    public void GivenDisputed() => _h.Disputed = true;

    [When("the program starts with the arguments {string}")]
    public Task WhenStarts(string arguments) => StartAsync(arguments, 1);

    [When("the program starts twice with the arguments {string}")]
    public Task WhenStartsTwice(string arguments) => StartAsync(arguments, 2);

    [Then("standard output holds {string}, {string}, {string}, {string} and {string}")]
    public void ThenOutputFive(string a, string b, string c, string d, string e) => OutputHolds(a, b, c, d, e);

    [Then("standard output holds {string}, {string}, {string} and {string}")]
    public void ThenOutputFour(string a, string b, string c, string d) => OutputHolds(a, b, c, d);

    [Then("standard output holds the line {string}")]
    public void ThenOutputLine(string line) => Assert.Contains(line, ConsoleHarness.Lines(_h.Out));

    [Then("standard output holds {string}")]
    public void ThenOutputOne(string a) => OutputHolds(a);

    [Then("standard output holds {string} and {string}")]
    public void ThenOutputTwo(string a, string b) => OutputHolds(a, b);

    [Then("standard error holds the line {string}")]
    public void ThenErrorHoldsLine(string line) => Assert.Contains(line, ConsoleHarness.Lines(_h.Error));

    [Then("standard error is the one line {string}")]
    public void ThenErrorIsLine(string line) => Assert.Equal(line, Assert.Single(ConsoleHarness.Lines(_h.Error)));

    [Then("standard error is one line that holds {string}")]
    public void ThenErrorIsOneLineHolding(string text) => Assert.Contains(text, Assert.Single(ConsoleHarness.Lines(_h.Error)), StringComparison.Ordinal);

    [Then("standard error is one line that holds {string} and {string}")]
    public void ThenErrorIsOneLineHoldingTwo(string a, string b)
    {
        string line = Assert.Single(ConsoleHarness.Lines(_h.Error));
        Assert.Contains(a, line, StringComparison.Ordinal);
        Assert.Contains(b, line, StringComparison.Ordinal);
    }

    [Then("standard error holds text")]
    public void ThenErrorText() => Assert.NotEmpty(_h.Error.ToString());

    [Then("standard error holds one line that holds {string}")]
    public void ThenErrorLine(string text) => Assert.Contains(text, Assert.Single(ConsoleHarness.Lines(_h.Error)), StringComparison.Ordinal);

    [Then("GitHub gets no review request")]
    public void ThenNoReviewRequest() => Assert.Empty(ReviewRequests);

    [Then("GitHub gets one GraphQL request that names {string} with the bot login {string}")]
    public void ThenReviewRequest(string name, string login)
    {
        TimedRequest request = Assert.Single(_h.Requests, r => r.Path.EndsWith("/graphql", StringComparison.Ordinal) && r.Content!.Contains(name, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Content!);
        Assert.Equal([login], json.RootElement.GetProperty("variables").GetProperty("botLogins").EnumerateArray().Select(e => e.GetString()));
    }

    [Then("the review request comes after the read of the head commit that gives the head of the work tree")]
    public void ThenAfterHeadRead()
    {
        int reads = 0;
        int gives = -1;
        for (int i = 0; i < _h.Requests.Count && gives < 0; i++)
        {
            if (IsHeadRead(_h.Requests[i]) && _h.HeadSha(++reads) == ConsoleHarness.DefaultSha)
            {
                gives = i;
            }
        }

        Assert.True(gives >= 0);
        Assert.True(_h.Requests.FindIndex(IsReviewRequest) > gives);
    }

    [Then("the review request comes before the first read of the check runs")]
    public void ThenBeforeCheckRuns()
    {
        int request = _h.Requests.FindIndex(IsReviewRequest);
        Assert.True(request >= 0);
        Assert.True(request < _h.Requests.FindIndex(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal)));
    }

    [Then("the review request comes 10 seconds or more after the push")]
    public void ThenRequestAfterPush() =>
        Assert.True(Assert.Single(ReviewRequests).At - _h.PushTimes.Last() >= TimeSpan.FromSeconds(10));

    [Then("standard error holds a line with the message of the GraphQL error")]
    public void ThenErrorGraphQl() =>
        Assert.Contains(ConsoleHarness.Lines(_h.Error), l => l.Contains(_h.ReviewRequestError!, StringComparison.Ordinal));

    [Then("GitHub gets no request")]
    public void ThenNoRequest() => Assert.Empty(_h.Requests);

    [Then("no program runs")]
    public void ThenNoProgram() => Assert.Empty(_h.Commands);

    [Then("GitHub gets a GET for the path {string}")]
    public void ThenGet(string path) => Assert.Contains(_h.Requests, r => r.Method is "GET" && r.Path == path);

    [Then("git clones {string} with the token of FACTORY_GITHUB_TOKEN")]
    public void ThenCloneToken(string url)
    {
        CommandSpec clone = Clones.First();
        Assert.Equal(url, clone.Arguments[1]);
        Assert.Equal(_h.Environment["FACTORY_GITHUB_TOKEN"], clone.EnvironmentVariables["FACTORY_GITHUB_TOKEN"]);
        Assert.DoesNotContain(_h.Commands, c => c.Arguments.Any(a => a.Contains(_h.Environment["FACTORY_GITHUB_TOKEN"], StringComparison.Ordinal)));
    }

    [Then("git clones into a directory under the temporary directory of the system")]
    public void ThenCloneTemp()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        foreach (CommandSpec clone in Clones)
        {
            Assert.StartsWith(temp, Path.GetFullPath(clone.Arguments[2]), StringComparison.Ordinal);
        }

        Assert.NotEmpty(Clones);
    }

    [Then("that directory is gone when the program exits")]
    public void ThenGone()
    {
        Assert.NotEmpty(_h.CloneDirectories);
        Assert.All(_h.CloneDirectories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Then("the pull request opens with the base {string}")]
    public void ThenBase(string branch)
    {
        TimedRequest open = Assert.Single(_h.Posts("/pulls"));
        using var json = JsonDocument.Parse(open.Content!);
        Assert.Equal(branch, json.RootElement.GetProperty("base").GetString());
    }

    [Then("git clones no repo")]
    public void ThenNoClone() => Assert.Empty(Clones);

    [Then("git pushes one branch whose name is factory\\/ and 8 lowercase hexadecimal characters")]
    public void ThenPushOne() => Assert.Matches(BranchName(), Assert.Single(Pushes()));

    [Then("git pushes two branches with different names")]
    public void ThenPushTwo()
    {
        List<string> pushes = Pushes();
        Assert.Equal(2, pushes.Count);
        Assert.NotEqual(pushes[0], pushes[1]);
    }

    [Then("claude runs the plan prompt with the model {string} and the turn limit {int}, the build prompt with the model {string} and the turn limit {int}, and the review prompt with the model {string} and the turn limit {int}")]
    public void ThenModels(string planModel, int planTurns, string buildModel, int buildTurns, string reviewModel, int reviewTurns)
    {
        Assert.Equal((planModel, planTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)), Prompt("plan"));
        Assert.Equal((buildModel, buildTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)), Prompt("build"));
        Assert.Equal((reviewModel, reviewTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)), Prompt("review"));
    }

    [Then("git commits with the author name {string} and the author email {string}")]
    public void ThenAuthor(string name, string email)
    {
        CommandSpec commit = _h.Commands.First(c => c.Command is "git" && c.Arguments[0] is "commit");
        Assert.Equal(name, commit.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal(name, commit.EnvironmentVariables["GIT_COMMITTER_NAME"]);
        Assert.Equal(email, commit.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
        Assert.Equal(email, commit.EnvironmentVariables["GIT_COMMITTER_EMAIL"]);
    }

    [Then("docker starts the container from the image {string}")]
    public void ThenImage(string tag) =>
        Assert.Contains(_h.Commands, c => c.Command is "docker" && c.Arguments[0] is "run" && c.Arguments.Contains(tag));

    [Then("docker builds the image {string} with the current directory of the program as its context")]
    public void ThenBuild(string tag)
    {
        CommandSpec build = Assert.Single(_h.Commands, c => c.Command is "docker" && c.Arguments[0] is "build");
        Assert.Equal(["--tag", tag], build.Arguments.Skip(1).Take(2));
        Assert.Equal(_h.Root, build.Arguments[^1]);
    }

    [Then("docker gives the name {string} to the command {string}")]
    public void ThenName(string name, string command)
    {
        List<string> seen = [];
        foreach (CommandSpec spec in _h.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(ConsoleHarness.ContainerId);
            if (string.Join(' ', arguments[(id + 1)..]) == command)
            {
                seen.AddRange(arguments[..id].Where(a => a is not ("exec" or "--env")));
            }
        }

        Assert.Equal([name], seen);
    }

    [Then("the program posts one comment on the issue {int}")]
    public void ThenIssueComment(int number) => Assert.Single(_h.Posts($"/issues/{number}/comments"));

    [Then("the program posts one comment on the pull request {int}")]
    public void ThenPullComment(int number) => Assert.Single(_h.Posts($"/issues/{number}/comments"));

    [Then("GitHub gets no read of the check runs")]
    public void ThenNoCheckReads() => Assert.Empty(CheckReads);

    [Then("GitHub gets a read of the check runs of {string}")]
    public void ThenCheckOf(string sha) => Assert.Contains(CheckReads, r => r.Path.Contains(sha, StringComparison.Ordinal));

    [Then("GitHub gets no read of the check runs of {string}")]
    public void ThenNoCheckOf(string sha) => Assert.DoesNotContain(CheckReads, r => r.Path.Contains(sha, StringComparison.Ordinal));

    [Then("GitHub gets a read of the check runs of {string} 10 seconds or more after the push")]
    public void ThenCheckAfterPush(string sha)
    {
        TimeSpan push = _h.PushTimes.Last();
        TimedRequest read = CheckReads.First(r => r.Path.Contains(sha, StringComparison.Ordinal));
        Assert.True(read.At - push >= TimeSpan.FromSeconds(10), $"read at {read.At}, push at {push}");
    }

    [Then("the second read of the check runs comes 30 seconds or more after the first")]
    public void ThenSecondRead()
    {
        List<TimedRequest> reads = [.. CheckReads];
        Assert.True(reads.Count >= 2);
        Assert.True(reads[1].At - reads[0].At >= TimeSpan.FromSeconds(30));
    }

    [Then("git clones {int} time(s) in the run")]
    public void ThenCloneCount(int count) => Assert.Equal(count, Clones.Count());

    [Then("git clones {string} a second time and checks out the branch {string} in it")]
    public void ThenSecondClone(string url, string branch)
    {
        List<CommandSpec> clones = [.. Clones];
        Assert.Equal(2, clones.Count);
        Assert.Equal(url, clones[1].Arguments[1]);
        CheckedOut(clones[1], branch);
    }

    [Then("git clones {string} and checks out the branch {string} in it")]
    public void ThenCloneCheckout(string url, string branch)
    {
        CommandSpec clone = Assert.Single(Clones);
        Assert.Equal(url, clone.Arguments[1]);
        CheckedOut(clone, branch);
    }

    [Then("claude runs no plan prompt")]
    public void ThenNoPlan() => Assert.DoesNotContain(_h.Prompts, p => p.Kind is "plan");

    [Then("the feedback run's build prompt runs with the model {string} and the turn limit {int}, and holds {string} and {string}")]
    public void ThenFeedbackPrompt(string model, int turns, string first, string second)
    {
        (string Kind, string Model, string Turns, string Text) prompt = Assert.Single(_h.Prompts, p => p.Kind is "build" && p.Text.Contains(".factory/threads.json", StringComparison.Ordinal));
        Assert.Equal(model, prompt.Model);
        Assert.Equal(turns.ToString(System.Globalization.CultureInfo.InvariantCulture), prompt.Turns);
        Assert.Contains(first, prompt.Text, StringComparison.Ordinal);
        Assert.Contains(second, prompt.Text, StringComparison.Ordinal);
    }

    [Then("the program resolves the thread {string}")]
    public void ThenResolves(string id) =>
        Assert.Contains(_h.Requests, r => r.Path.EndsWith("/graphql", StringComparison.Ordinal)
            && r.Content!.Contains("resolveReviewThread", StringComparison.Ordinal)
            && r.Content.Contains(id, StringComparison.Ordinal));

    [Then("GitHub gets a read of the check runs after the last reply")]
    public void ThenAfterReply()
    {
        int reply = _h.Requests.FindLastIndex(r => r.Path.EndsWith("/graphql", StringComparison.Ordinal)
            && r.Content!.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal));
        int read = _h.Requests.FindLastIndex(r => r.Path.EndsWith("/check-runs", StringComparison.Ordinal));
        Assert.True(reply >= 0);
        Assert.True(read > reply);
    }

    private async Task StartAsync(string arguments, int times)
    {
        string[] args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (int build in _factory.Harness.RedBuilds)
        {
            _h.RedBuilds.Add(build);
        }

        if (_state.NoReviews)
        {
            _h.Reviews = ConsoleHarness.NoReviewsJson();
        }

        if (_state.NoCheckRuns)
        {
            _h.CheckRuns = _ => """{"check_runs":[]}""";
        }

        if (_state.FirstCheckRun is { } first)
        {
            Func<int, string> later = _h.CheckRuns;
            _h.CheckRuns = read => read is 1 ? ConsoleHarness.RunsJson(first.Name, first.Status, null) : later(read);
        }

        for (int i = 0; i < times; i++)
        {
            _state.ExitCode = await _h.RunAsync(args, TestContext.Current.CancellationToken);
        }
    }

    private List<string> Pushes() =>
        [.. _h.Commands.Where(c => c.Command is "git" && c.Arguments[0] is "push").Select(c => c.Arguments[2])];

    private (string Model, string Turns) Prompt(string kind)
    {
        (string Kind, string Model, string Turns, string Text) prompt = _h.Prompts.First(p => p.Kind == kind);
        return (prompt.Model, prompt.Turns);
    }

    private void CheckedOut(CommandSpec clone, string branch) =>
        Assert.Contains(_h.Commands, c => c.Command is "git" && c.Arguments is ["checkout", _] && c.Arguments[1] == branch && c.WorkingDirectory == clone.Arguments[2]);

    private void OutputHolds(params string[] values)
    {
        string output = _h.Out.ToString();
        foreach (string value in values)
        {
            Assert.Contains(value, output, StringComparison.Ordinal);
        }
    }
}
