using System.Text;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FactoryRunSteps(PullRequestAnswer answer, ReviewPromptState promptState) : IDisposable
{
    private readonly Specs.Unit.FactoryRunHarness _h = new();
    private FactoryRunResult? _result;
    private Exception? _exception;

    /// <summary>The harness whose gate, review and standard output settings the shared steps of this class fill.</summary>
    public Specs.Unit.FactoryRunHarness Harness => _h;

    /// <summary>The commands of another run, so a feature of that run shares the git steps of this class.</summary>
    public List<CommandSpec>? OtherCommands { get; set; }

    /// <summary>The git commands and session requests of another run, in order, so a feature of that run shares the steps of the managed path.</summary>
    public List<string>? OtherTimeline { get; set; }

    /// <summary>The prompts of another run, so a feature of that run shares the prompt steps of this class.</summary>
    public List<Specs.Unit.SeenPrompt>? OtherPrompts { get; set; }

    public void Dispose() => _h.Dispose();

    private List<CommandSpec> Commands => OtherCommands ?? _h.Commands;

    private List<string> Timeline => OtherTimeline ?? _h.Timeline;

    /// <summary>The result of the run, for a step class of another feature of the same run.</summary>
    public FactoryRunResult LastResult => Result;

    private FactoryRunResult Result => _result ?? throw new InvalidOperationException("the run gave no result");

    private IEnumerable<Specs.Unit.SeenPrompt> Builds => _h.Prompts.Where(p => p.Kind is "build");

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

    [Given("the agent writes the feature file and the plan on the plan prompt")]
    public void GivenAgentWrites() => _h.AgentWritesPlan = true;

    [Given("claude answers each prompt with the error flag clear")]
    public void GivenFlagClear() => _h.BuildIsError = false;

    [Given("each dotnet command exits with the code 0 and the TRX file holds one passed test")]
    public void GivenDotnet() => _h.RedBuilds.Clear();

    [Given("the agent approves on the review prompt")]
    public void GivenApproves() => _h.RejectedBuilds.Clear();

    [Given("the clone lacks {string}")]
    public void GivenLacks(string path) => _h.Lacks.Add(path);

    [Given("the agent writes no {string} on the plan prompt and on its correction")]
    public void GivenNoPlan(string path)
    {
        Assert.Equal(".factory/plan.md", path);
        _h.AgentWritesPlan = false;
    }

    [Given("the run's list of secret names is {string} and {string}")]
    public void GivenSecrets(string first, string second) => _h.SecretNames = [first, second];

    [Given("claude answers the build prompt with the error flag set")]
    public void GivenBuildError() => _h.BuildIsError = true;

    [Given("the test gate is red after the first build and passes after the second build")]
    public void GivenRedFirst() => _h.RedBuilds.Add(1);

    [Given("the review rejects the first build and approves the second build")]
    public void GivenRejectFirst() => _h.RejectedBuilds.Add(1);

    [Given("the test gate is red after the first build")]
    public void GivenRedOne() => _h.RedBuilds.Add(1);

    [Given("the review rejects the second build")]
    public void GivenRejectSecond() => _h.RejectedBuilds.Add(2);

    [Given("the test gate is red after the third build")]
    public void GivenRedThird() => _h.RedBuilds.Add(3);

    [Given("the review approves the fourth build")]
    public void GivenApproveFourth() => _h.RejectedBuilds.Remove(4);

    [Given("the test gate is red after each of four builds")]
    public void GivenRedAll()
    {
        foreach (int build in new[] { 1, 2, 3, 4 })
        {
            _h.RedBuilds.Add(build);
        }
    }

    [Given("the test gate is red after the first build and the third build")]
    public void GivenRedFirstThird()
    {
        _h.RedBuilds.Add(1);
        _h.RedBuilds.Add(3);
    }

    [Given("the review rejects the second build and the fourth build")]
    public void GivenRejectSecondFourth()
    {
        _h.RejectedBuilds.Add(2);
        _h.RejectedBuilds.Add(4);
    }

    [Given("the test command writes the letter {string} {int} times to its standard output")]
    public void GivenStdout(string letter, int count) => _h.TestStdout = string.Concat(Enumerable.Repeat(letter, count));

    [Given("the run uses Managed Agents sessions")]
    public void GivenManaged() => _h.UseManaged();

    [Given("the session pushes no commit on the first build")]
    public void GivenNoPush() => _h.NoPushBuilds.Add(1);

    [Given("the session branch tracks the file {string}")]
    public void GivenTracked(string path) => _h.Managed!.TrackedFactoryFiles.Add(path);

    [Given("the issue holds a block under ## Scenarios for {string}")]
    public void GivenScenariosBlock(string path) =>
        _h.Body += $"\n\n## Scenarios\n\n```gherkin\n# {path}\nFeature: Checkout total\n\n  Scenario: An empty cart totals zero\n    Given an empty cart\n```\n";

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
            _result = await _h.RunAsync(TestContext.Current.CancellationToken);
            promptState.Text = _h.Prompts.FirstOrDefault(p => p.Kind is "review")?.Text;
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
        Specs.Unit.SeenPrompt prompt = Assert.Single(_h.Prompts, p => p.Model == model);
        Assert.Equal(turns.ToString(System.Globalization.CultureInfo.InvariantCulture), prompt.Turns);
        Assert.Contains(first, prompt.Text, StringComparison.Ordinal);
        Assert.Contains(second, prompt.Text, StringComparison.Ordinal);
    }

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int}")]
    public void ThenPrompt(string model, int turns)
    {
        Specs.Unit.SeenPrompt prompt = Assert.Single(_h.Prompts, p => p.Model == model);
        Assert.Equal(turns.ToString(System.Globalization.CultureInfo.InvariantCulture), prompt.Turns);
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
        Assert.Empty(_h.Prompts);
    }

    [Then("FactoryRun gives back a result with no pull request and {int} Redo(s)")]
    public void ThenNoPull(int redos)
    {
        Assert.Null(Result.PullRequest);
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
        Specs.Unit.SeenRequest open = Assert.Single(_h.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
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
        Assert.Equal(new PullRequest(number, url), Result.PullRequest);
        Assert.Equal(redos, Result.Redos);
    }

    [Then("FactoryRun gives back a result with the pull request of the number {int} and {int} Redo(s)")]
    public void ThenPullNumber(int number, int redos)
    {
        Assert.Equal(number, Result.PullRequest?.Number);
        Assert.Equal(redos, Result.Redos);
    }

    [Then("docker gives the names {string} and {string} to the command {string}")]
    public void ThenNames(string first, string second, string command)
    {
        foreach (CommandSpec spec in Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(Specs.Unit.FactoryRunHarness.ContainerId);
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
    public void ThenSecondBuild(string text) => Assert.Contains(text, Builds.ElementAt(1).Text, StringComparison.Ordinal);

    [Then("FactoryRun runs the second build prompt before any prompt with the model {string}")]
    public void ThenSecondBeforeReview(string model)
    {
        int second = _h.Prompts.FindIndex(p => p.Kind is "build" && p.BuildsBefore is 2);
        int review = _h.Prompts.FindIndex(p => p.Model == model);
        Assert.True(second >= 0);
        Assert.True(review > second);
    }

    [Then("FactoryRun runs {int} build prompts in the run")]
    public void ThenBuildCount(int count) => Assert.Equal(count, Builds.Count());

    [Then("git pushes no branch")]
    public void ThenNoPush() => Assert.DoesNotContain(Commands, c => c.Command is "git" && c.Arguments[0] is "push");

    [Then("FactoryRun posts one comment on the issue that holds {string} and gives back a result with no pull request and {int} Redo(s)")]
    public void ThenCommentAndResult(string text, int redos)
    {
        Assert.Contains(text, Assert.Single(_h.CommentBodies), StringComparison.Ordinal);
        Assert.Null(Result.PullRequest);
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
        Assert.Equal(["push", "comment", "rm"], _h.Events);

    [Then("FactoryRun throws GitHubRequestException and removes the container")]
    public void ThenThrows()
    {
        Assert.IsType<GitHubRequestException>(_exception);
        Assert.Contains("rm", _h.Events);
    }

    private int FirstPush(string branch)
    {
        int push = Commands.FindIndex(c => c.Command is "git" && c.Arguments is ["push", "origin", var name] && name == branch);
        Assert.True(push >= 0, $"no push of {branch}");
        return push;
    }

    [Then("the docker run of the run holds neither {string} nor {string}")]
    public void ThenDockerRunLacks(string first, string second)
    {
        CommandSpec run = Assert.Single(Commands, c => c.Command is "docker" && c.Arguments[0] is "run");
        Assert.DoesNotContain(run.Arguments, argument => argument.Contains(first, StringComparison.Ordinal));
        Assert.DoesNotContain(run.Arguments, argument => argument.Contains(second, StringComparison.Ordinal));
    }

    [Then("the docker run of the run mounts the clone at \\/work and names the image {string}")]
    public void ThenDockerRunMountsTheClone(string image)
    {
        CommandSpec run = Assert.Single(Commands, c => c.Command is "docker" && c.Arguments[0] is "run");
        CommandSpec clone = Assert.Single(Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
        Assert.Contains($"{clone.Arguments[2]}:/work", run.Arguments);
        Assert.Contains(image, run.Arguments);
    }

    [Then("the arguments of the docker run of the run hold {string} and then {string}, and {string} and then {string}")]
    public void ThenDockerRunHolds(string firstFlag, string firstValue, string secondFlag, string secondValue)
    {
        CommandSpec run = Assert.Single(Commands, c => c.Command is "docker" && c.Arguments[0] is "run");
        List<string> arguments = [.. run.Arguments];
        foreach ((string flag, string value) in new[] { (firstFlag, firstValue), (secondFlag, secondValue) })
        {
            int index = arguments.IndexOf(value);
            Assert.True(index > 0 && arguments[index - 1] == flag, $"docker run: {string.Join(' ', arguments)}");
        }
    }

    [Then("every dotnet command of the run goes through docker exec")]
    public void ThenDotnetInContainer()
    {
        Assert.DoesNotContain(Commands, c => c.Command is "dotnet");
        Assert.Contains(Commands, c => c.Command is "docker" && c.Arguments[0] is "exec" && c.Arguments.Contains("dotnet"));
    }

    [Then("the API printer runs through docker exec")]
    public void ThenPrinterInContainer() =>
        Assert.Contains(Commands, c => c.Command is "docker" && c.Arguments[0] is "exec" && c.Arguments.Contains("/opt/api-printer/ApiPrinter.dll"));

    [Then("the run removes the container")]
    public void ThenContainerRemoved() => Assert.Contains("rm", _h.Events);

    [Then("git pushes the session branch {string} before the first session starts")]
    public void ThenSessionBranchFirst(string branch)
    {
        int push = Timeline.IndexOf($"git push origin {branch}");
        int start = Timeline.IndexOf("POST /v1/sessions");
        Assert.True(push >= 0, $"no push of {branch}: {string.Join(" | ", Timeline)}");
        Assert.True(start > push, $"the first session started before the push of {branch}");
    }

    [Then("git commits the work tree with the message {string} before the first push of {string}")]
    public void ThenCommitsBeforePush(string message, string branch)
    {
        int push = FirstPush(branch);
        int commit = Commands.FindIndex(c => c.Command is "git" && c.Arguments is ["commit", "-m", var text] && text == message);
        Assert.InRange(commit, 0, push - 1);
    }

    [Then("git runs no commit before the first push of {string}")]
    public void ThenNoCommitBeforePush(string branch) =>
        Assert.DoesNotContain(Commands.Take(FirstPush(branch)), c => c.Command is "git" && c.Arguments[0] is "commit");

    [Then("git pulls the branch {string} {int} times")]
    public void ThenPulls(string branch, int count) =>
        Assert.Equal(count, Commands.Count(c => c.Command is "git" && c.Arguments[0] is "pull" && c.Arguments[^1] == branch));

    [Then("git deletes the branch {string} on origin after the push of {string}")]
    public void ThenDeletedAfter(string branch, string pushed)
    {
        int push = Timeline.IndexOf($"git push origin {pushed}");
        int delete = Timeline.FindIndex(line => IsDelete(line, branch));
        Assert.True(push >= 0, $"no push of {pushed}: {string.Join(" | ", Timeline)}");
        Assert.True(delete > push, $"git commands: {string.Join(" | ", Timeline)}");
    }

    [Then("git deletes the branch {string} on origin as its last git command")]
    public void ThenDeletedLast(string branch) =>
        Assert.True(IsDelete(Timeline.Last(line => line.StartsWith("git ", StringComparison.Ordinal)), branch), $"git commands: {string.Join(" | ", Timeline)}");

    [Then("git pushes no branch {string}")]
    public void ThenNoPushOf(string branch) =>
        Assert.DoesNotContain(Commands, c => c.Command is "git" && c.Arguments is ["push", "origin", var name] && name == branch);

    [Then("git checks out {string}, pulls {string} into it and pushes {string}, in this order")]
    public void ThenMoves(string branch, string session, string pushed)
    {
        int checkout = Timeline.LastIndexOf($"git checkout {branch}");
        int pull = Timeline.FindIndex(Math.Max(checkout, 0), line => line.StartsWith("git pull", StringComparison.Ordinal) && line.EndsWith(" " + session, StringComparison.Ordinal));
        int push = Timeline.FindIndex(Math.Max(pull, 0), line => line == $"git push origin {pushed}");
        Assert.True(checkout >= 0 && pull > checkout && push > pull, $"git commands: {string.Join(" | ", Timeline)}");
    }

    private static bool IsDelete(string line, string branch) =>
        line.StartsWith("git push", StringComparison.Ordinal) && line.Contains("--delete", StringComparison.Ordinal) && line.EndsWith(" " + branch, StringComparison.Ordinal);
}
