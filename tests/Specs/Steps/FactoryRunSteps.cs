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

    public void Dispose() => _h.Dispose();

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
        catch (GitHubRequestException ex)
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
        Assert.Contains(first, prompt.Text);
        Assert.Contains(second, prompt.Text);
    }

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int}")]
    public void ThenPrompt(string model, int turns)
    {
        Specs.Unit.SeenPrompt prompt = Assert.Single(_h.Prompts, p => p.Model == model);
        Assert.Equal(turns.ToString(System.Globalization.CultureInfo.InvariantCulture), prompt.Turns);
    }

    [Then("FactoryRun posts one comment on the issue that holds {string}")]
    public void ThenComment(string text) => Assert.Contains(text, Assert.Single(_h.CommentBodies));

    [Then("FactoryRun posts one comment on the issue that holds {string} and {string}")]
    public void ThenCommentTwo(string first, string second)
    {
        string body = Assert.Single(_h.CommentBodies);
        Assert.Contains(first, body);
        Assert.Contains(second, body);
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
        CommandSpec commit = Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "commit");
        Assert.Equal(message, commit.Arguments[2]);
        Assert.Equal(name, commit.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal(email, commit.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
    }

    [Then("git pushes the branch {string}")]
    public void ThenPush(string branch)
    {
        CommandSpec push = Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "push");
        Assert.Equal(["push", "origin", branch], push.Arguments);
    }

    [Then("FactoryRun opens a pull request on {string} from the branch {string} into {string} with the title {string}")]
    public void ThenOpens(string slug, string head, string baseBranch, string title)
    {
        Specs.Unit.SeenRequest open = Assert.Single(_h.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
        Assert.EndsWith($"/repos/{slug}/pulls", open.Uri);
        using var json = System.Text.Json.JsonDocument.Parse(open.Content!);
        Assert.Equal(head, json.RootElement.GetProperty("head").GetString());
        Assert.Equal(baseBranch, json.RootElement.GetProperty("base").GetString());
        Assert.Equal(title, json.RootElement.GetProperty("title").GetString());
    }

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
        foreach (CommandSpec spec in _h.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(Specs.Unit.FactoryRunHarness.ContainerId);
            string line = string.Join(' ', arguments[(id + 1)..]);
            List<string> names = arguments[..id].Where(a => a is not ("exec" or "--env")).ToList();
            if (line == command)
            {
                Assert.Equal([first, second], names);
            }
            else
            {
                Assert.Empty(names);
            }
        }

        Assert.Contains(_h.Commands, c => c.Arguments.Contains("--project") && c.Arguments.Contains("tests/Integration"));
    }

    [Then("FactoryRun runs a second build prompt that holds {string}")]
    public void ThenSecondBuild(string text) => Assert.Contains(text, Builds.ElementAt(1).Text);

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
    public void ThenNoPush() => Assert.DoesNotContain(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "push");

    [Then("FactoryRun posts one comment on the issue that holds {string} and gives back a result with no pull request and {int} Redo(s)")]
    public void ThenCommentAndResult(string text, int redos)
    {
        Assert.Contains(text, Assert.Single(_h.CommentBodies));
        Assert.Null(Result.PullRequest);
        Assert.Equal(redos, Result.Redos);
    }

    [Then("FactoryRun posts one comment of {int} bytes or fewer that holds the letter {string} {int} times in sequence and {string}")]
    public void ThenCut(int limit, string letter, int count, string marker)
    {
        string body = Assert.Single(_h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= limit);
        Assert.Contains(string.Concat(Enumerable.Repeat(letter, count)), body);
        Assert.Contains(marker, body);
    }

    [Then("FactoryRun removes the container after the delivery")]
    public void ThenRemoved()
    {
        Assert.Equal(["push", "comment", "rm"], _h.Events);
    }

    [Then("FactoryRun throws GitHubRequestException and removes the container")]
    public void ThenThrows()
    {
        Assert.IsType<GitHubRequestException>(_exception);
        Assert.Contains("rm", _h.Events);
    }
}
