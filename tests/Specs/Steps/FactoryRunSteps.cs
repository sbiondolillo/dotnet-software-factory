using System.Text;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FactoryRunSteps(PullRequestAnswer answer, ReviewPromptState promptState) : IDisposable
{
    private readonly RunHarness _h = new();
    private RunResult<Delivered>? _result;
    private Exception? _exception;

    /// <summary>The harness of the run, which the step classes of a factory run and of a feedback run share.</summary>
    public RunHarness Harness => _h;

    /// <summary>The commands of the console program, so a scenario of the program shares the git steps of this class.</summary>
    public List<CommandSpec>? OtherCommands { get; set; }

    public void Dispose() => _h.Dispose();

    private List<CommandSpec> Commands => OtherCommands ?? _h.Commands;

    private List<string> Timeline => _h.Timeline;

    /// <summary>The result of the run, for a step class of another feature of the same run.</summary>
    public RunResult<Delivered> LastResult => Result;

    private RunResult<Delivered> Result => _result ?? throw new InvalidOperationException("the run gave no result");

    private IEnumerable<AgentRequest> Builds => _h.Builder.Of(Phase.Build);

    [Given("the repo {string} of the owner {string}, the issue {int} and the token {string}")]
    public void GivenRepo(string repo, string owner, int issue, string token)
    {
        _h.Repo = repo;
        _h.Owner = owner;
        _h.Issue = issue;
        _h.Token = token;
    }

    [Given("the run id {string}, the base branch {string} and an empty list of secret names")]
    public void GivenRun(string runId, string baseBranch)
    {
        _h.RunId = runId;
        _h.BaseBranch = baseBranch;
        _h.SecretNames = [];
    }

    [Given("the plan model {string} with turn limit {int}, the build model {string} with turn limit {int} and the review model {string} with turn limit {int}")]
    public void GivenModels(string plan, int planTurns, string build, int buildTurns, string review, int reviewTurns)
    {
        _h.PlanModel = plan;
        _h.PlanTurns = planTurns;
        _h.BuildModel = build;
        _h.BuildTurns = buildTurns;
        _h.ReviewModel = review;
        _h.ReviewTurns = reviewTurns;
    }

    [Given("the author name {string} and the author email {string}")]
    public void GivenAuthor(string name, string email)
    {
        _h.AuthorName = name;
        _h.AuthorEmail = email;
    }

    [Given("GitHub answers the read of the issue with the title {string} and the body {string}")]
    public void GivenIssue(string title, string body)
    {
        _h.Title = title;
        _h.Body = body;
    }

    [Given("the clone holds the four paths of the layout and its head commit is {string}")]
    public void GivenClone(string sha) => _h.HeadSha = sha;

    [Given("git reports a change in the work tree after each build")]
    public void GivenChange() => _h.ChangeAfterBuild = true;

    /// <summary>The builder writes both on the plan prompt by default, so the step sets nothing.</summary>
    [Given("the agent writes the feature file and the plan on the plan prompt")]
    public static void GivenAgentWrites()
    {
    }

    /// <summary>The fake agents answer each prompt with the error flag clear by default, so the step sets nothing.</summary>
    [Given("claude answers each prompt with the error flag clear")]
    public static void GivenFlagClear()
    {
    }

    [Given("each dotnet command exits with the code 0 and the TRX file holds one passed test")]
    public void GivenDotnet() => _h.RedBuilds.Clear();

    /// <summary>The reviewer approves every review by default, so the step sets nothing.</summary>
    [Given("the agent approves on the review prompt")]
    public static void GivenApproves()
    {
    }

    [Given("the clone lacks {string}")]
    public void GivenLacks(string path) => _h.Lacks.Add(path);

    [Given("the agent writes no {string} on the plan prompt and on its correction")]
    public void GivenNoPlan(string path)
    {
        Assert.Equal(".factory/plan.md", path);
        _h.Builder.Clear(Phase.Plan);
    }

    [Given("the run's list of secret names is {string} and {string}")]
    public void GivenSecrets(string first, string second) => _h.SecretNames = [first, second];

    [Given("the test gate is red after the first build and passes after the second build")]
    public void GivenRedFirst() => _h.RedBuilds.Add(1);

    [Given("the test gate is red after the first build")]
    public void GivenRedOne() => _h.RedBuilds.Add(1);

    [Given("the test gate is red after each of four builds")]
    public void GivenRedAll()
    {
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            _h.RedBuilds.Add(build);
        }
    }

    [Given("the test command writes the letter {string} {int} times to its standard output")]
    public void GivenStdout(string letter, int count) => _h.TestStdout = string.Concat(Enumerable.Repeat(letter, count));

    [Given("the builder stops the run on the first build with the text {string}")]
    public void GivenStop(string text) => _h.Builder.Throw(Phase.Build, new SessionBranchException(text), 1);

    [Given("the first build answer carries the fault {string}")]
    public void GivenFault(string fault) => _h.Builder.Answer(Phase.Build, FakeAgent.Done() with { Faults = [fault] }, 1);

    [Given("the caller gives the base commit {string}")]
    public void GivenBase(string commit) => _h.BaseCommit = commit;

    [When("a caller starts a run")]
    public async Task WhenStarts()
    {
        _h.PullNumber = answer.Number;
        _h.PullUrl = answer.Url;
        _h.PullStatus = answer.Status;
        try
        {
            _result = await _h.RunFactoryAsync(TestContext.Current.CancellationToken);
            promptState.Text = _h.Reviewer.Of(Phase.Review).FirstOrDefault()?.Prompt.Text;
        }
        catch (Exception ex) when (ex is GitHubRequestException or ClaudeCodeException)
        {
            _exception = ex;
        }
    }

    [Then("git clones {string} into the directory that the caller gave")]
    public void ThenClones(string url)
    {
        CommandSpec clone = Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
        Assert.Equal(url, clone.Arguments[1]);
        Assert.Equal(_h.CloneDirectory, clone.Arguments[2]);
    }

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int} that holds {string} and {string}")]
    public void ThenPromptHolds(string model, int turns, string first, string second)
    {
        AgentRequest prompt = Assert.Single(_h.AgentRequests, r => r.Model == model);
        Assert.Equal(turns, prompt.MaxTurns);
        Assert.Contains(first, prompt.Prompt.Text, StringComparison.Ordinal);
        Assert.Contains(second, prompt.Prompt.Text, StringComparison.Ordinal);
    }

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int}")]
    public void ThenPrompt(string model, int turns)
    {
        AgentRequest prompt = Assert.Single(_h.AgentRequests, r => r.Model == model);
        Assert.Equal(turns, prompt.MaxTurns);
    }

    [Then("FactoryRun posts one comment on the issue that holds {string}")]
    public void ThenComment(string text) => Assert.Contains(text, Assert.Single(_h.CommentBodies), StringComparison.Ordinal);

    [Then("FactoryRun posts one comment on the issue that holds {string} and {string}")]
    public void ThenCommentTwo(string first, string second)
    {
        string body = Assert.Single(_h.CommentBodies);
        Assert.Contains(first, body, StringComparison.Ordinal);
        Assert.Contains(second, body, StringComparison.Ordinal);
    }

    [Then("FactoryRun starts no container and runs no prompt")]
    public void ThenNoContainer()
    {
        Assert.DoesNotContain(_h.Commands, c => c.Command is "docker");
        Assert.Empty(_h.AgentRequests);
    }

    [Then("FactoryRun gives back a result with no pull request and {int} Redo(s)")]
    public void ThenNoPull(int redos)
    {
        Assert.Null(Result.Passed);
        Assert.Equal(redos, Result.Redos);
    }

    [Then("FactoryRun runs no build prompt")]
    public void ThenNoBuild() => Assert.Empty(Builds);

    [Then("git commits the change of the build with the message {string}, the author name {string} and the author email {string}")]
    public void ThenCommit(string message, string name, string email)
    {
        CommandSpec commit = Assert.Single(Commands, c => c.Command is "git" && c.Arguments[0] is "commit");
        Assert.Equal(message, commit.Arguments[2]);
        Assert.Equal(name, commit.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal(email, commit.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
    }

    [Then("git pushes the branch {string}")]
    public void ThenPush(string branch)
    {
        CommandSpec push = Assert.Single(Commands, c => c.Command is "git" && c.Arguments[0] is "push");
        Assert.Equal(["push", "origin", branch], push.Arguments);
    }

    [Then("FactoryRun opens a pull request on {string} from the branch {string} into {string} with the title {string}")]
    public void ThenOpens(string slug, string head, string baseBranch, string title)
    {
        SeenRequest open = Assert.Single(_h.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
        Assert.EndsWith($"/repos/{slug}/pulls", open.Uri, StringComparison.Ordinal);
        using var json = System.Text.Json.JsonDocument.Parse(open.Content!);
        Assert.Equal(head, json.RootElement.GetProperty("head").GetString());
        Assert.Equal(baseBranch, json.RootElement.GetProperty("base").GetString());
        Assert.Equal(title, json.RootElement.GetProperty("title").GetString());
    }

    [Then("git runs {string} in the directory that the caller gave, after the clone and before the head read")]
    public void ThenRunsBetween(string command)
    {
        List<CommandSpec> git = [.. _h.Commands.Where(c => c.Command is "git")];
        int clone = git.FindIndex(c => c.Arguments[0] is "clone");
        int found = git.FindIndex(c => string.Join(' ', c.Arguments) == command);
        int read = git.FindIndex(c => c.Arguments[0] is "rev-parse");
        Assert.True(clone >= 0 && found > clone && read > found, $"git commands: {string.Join(" | ", git.Select(c => string.Join(' ', c.Arguments)))}");
        Assert.Equal(_h.CloneDirectory, git[found].WorkingDirectory);
    }

    [Then("git runs no {string}")]
    public void ThenRunsNo(string prefix) =>
        Assert.DoesNotContain(_h.Commands, c => c.Command is "git" && string.Join(' ', c.Arguments).StartsWith(prefix, StringComparison.Ordinal));

    [Then("FactoryRun gives back a result with the pull request of the number {int} and the web address {string}, and {int} Redo(s)")]
    public void ThenPullUrl(int number, string url, int redos)
    {
        Assert.Equal(new PullRequest(number, url), Result.Passed?.PullRequest);
        Assert.Equal(redos, Result.Redos);
    }

    [Then("docker gives the names {string} and {string} to the command {string}")]
    public void ThenNames(string first, string second, string command)
    {
        foreach (CommandSpec spec in Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(GateSandbox.ContainerId);
            string line = string.Join(' ', arguments[(id + 1)..]);
            List<string> names = [.. arguments[..id].Where(a => a is not ("exec" or "--env"))];
            if (line == command)
            {
                Assert.Equal([first, second], names);
            }
            else
            {
                Assert.Empty(names);
            }
        }

        Assert.Contains(Commands, c => c.Arguments.Contains("--project") && c.Arguments.Contains("tests/Integration"));
    }

    [Then("FactoryRun runs a second build prompt that holds {string}")]
    public void ThenSecondBuild(string text) => Assert.Contains(text, Builds.ElementAt(1).Prompt.Text, StringComparison.Ordinal);

    [Then("git pushes no branch")]
    public void ThenNoPush() => Assert.DoesNotContain(Commands, c => c.Command is "git" && c.Arguments[0] is "push");

    [Then("FactoryRun posts one comment on the issue that holds {string} and gives back a result with no pull request and {int} Redo(s)")]
    public void ThenCommentAndResult(string text, int redos)
    {
        Assert.Contains(text, Assert.Single(_h.CommentBodies), StringComparison.Ordinal);
        Assert.Null(Result.Passed);
        Assert.Equal(redos, Result.Redos);
    }

    [Then("FactoryRun posts one comment of {int} bytes or fewer that holds the letter {string} {int} times in sequence and {string}")]
    public void ThenCut(int limit, string letter, int count, string marker)
    {
        string body = Assert.Single(_h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= limit);
        Assert.Contains(string.Concat(Enumerable.Repeat(letter, count)), body, StringComparison.Ordinal);
        Assert.Contains(marker, body, StringComparison.Ordinal);
    }

    [Then("FactoryRun removes the container after the delivery")]
    public void ThenRemoved() =>
        Assert.Equal(["push", "comment", "rm"], _h.Events.Where(e => e is "push" or "comment" or "rm"));

    [Then("FactoryRun throws GitHubRequestException and removes the container")]
    public void ThenThrows()
    {
        Assert.IsType<GitHubRequestException>(_exception);
        Assert.Contains("rm", _h.Events);
    }

    [Then("every dotnet command of the run goes through docker exec")]
    public void ThenDotnetInContainer()
    {
        Assert.DoesNotContain(Commands, c => c.Command is "dotnet");
        List<CommandSpec> dotnet = [.. Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec" && c.Arguments.Contains("dotnet"))];
        Assert.NotEmpty(dotnet);
        Assert.All(dotnet, c => Assert.Equal(GateSandbox.ContainerId, c.Arguments[c.Arguments.ToList().IndexOf("dotnet") - 1]));
    }

    [Given("the backend has the sessions {string} of the builder at {int} cents and {string} of the reviewer at {int} cents")]
    public void GivenSessions(string builder, int builderCents, string reviewer, int reviewerCents)
    {
        _h.Sessions.Add(new SessionCost(builder, "builder", builderCents));
        _h.Sessions.Add(new SessionCost(reviewer, "reviewer", reviewerCents));
    }

    [Then("the job summary of the run ends with the lines")]
    public void ThenSummaryEnds(string lines) =>
        Assert.EndsWith(lines.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", _h.Summary, StringComparison.Ordinal);

    [Then("the body of the pull request of the run ends with the line {string}")]
    public void ThenBodyEnds(string line) =>
        Assert.Equal(line, Assert.IsType<string>(_h.OpenedBody).TrimEnd('\n').Split('\n')[^1]);

    [Then("the run hands the backend the session branch {string}, the base commit {string} and the title {string}")]
    public void ThenStarted(string branch, string baseCommit, string title)
    {
        BackendStart start = Assert.IsType<BackendStart>(_h.Started);
        Assert.Equal((branch, baseCommit, title), (start.ScratchBranch, start.BaseCommit, start.Title));
    }

    [Then("the run runs no command in the container")]
    public void ThenNoExec() => Assert.DoesNotContain(_h.Commands, c => c.Command is "docker" && c.Arguments[0] is "exec");

    [Then("the backend collects onto {string} before git pushes {string}")]
    public void ThenCollectsBeforePush(string branch, string pushed)
    {
        int collect = Timeline.IndexOf($"collect {branch}");
        int push = Timeline.IndexOf($"git push origin {pushed}");
        Assert.True(collect >= 0 && push > collect, $"steps: {string.Join(" | ", Timeline)}");
    }

    [Then("git pushes no branch {string}")]
    public void ThenNoPushOf(string branch) =>
        Assert.DoesNotContain(Commands, c => c.Command is "git" && c.Arguments is ["push", "origin", var name] && name == branch);
}
