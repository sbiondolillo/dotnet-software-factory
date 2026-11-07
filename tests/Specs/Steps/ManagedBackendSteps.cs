using System.Globalization;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ManagedBackendSteps : IDisposable
{
    private const string Branch = ManagedBackendHarness.SessionBranch;

    private readonly ManagedBackendHarness _h = new();
    private SessionBranchException? _stop;

    public void Dispose() => _h.Dispose();

    private string Commands => $"commands: {string.Join(" | ", _h.Timeline)}";

    private List<string> Git => [.. _h.Timeline.Where(line => line.StartsWith("git ", StringComparison.Ordinal))];

    private static bool IsPrompt(string line) =>
        line is "POST /v1/sessions" || (line.StartsWith("POST /v1/sessions/", StringComparison.Ordinal) && line.EndsWith("/events", StringComparison.Ordinal));

    private static bool IsPull(string line) => line.StartsWith("git pull ", StringComparison.Ordinal) && line.EndsWith(" " + Branch, StringComparison.Ordinal);

    private static bool IsDelete(string line) =>
        line.StartsWith("git push ", StringComparison.Ordinal) && line.Contains("--delete", StringComparison.Ordinal);

    private int FirstPush()
    {
        int push = _h.Timeline.IndexOf($"git push origin {Branch}");
        Assert.True(push >= 0, Commands);
        return push;
    }

    private CommandSpec DockerRun() => Assert.Single(_h.Commands, c => c.Command is "docker" && c.Arguments[0] is "run");

    [Given("a managed backend over a clone, with the session branch {string}")]
    public static void GivenBackend(string branch) => Assert.Equal(Branch, branch);

    [Given("git reports a change in the work tree of the backend")]
    public void GivenChange() => _h.WorkTreeChanged = true;

    [Given("the session pushes no commit on the build")]
    public void GivenNoPush() => _h.Builder.Act(Phase.Build, _ => _h.Api.HoldHead());

    [Given("the session branch tracks the file {string}")]
    public void GivenTracked(string path) => _h.Api.TrackedFactoryFiles.Add(path);

    [Given("the pull after the build brings a commit that tracks the file {string}")]
    public void GivenTrackedOnPull(string path) => _h.Builder.Act(Phase.Build, _ => _h.Api.TrackOnNextPull(path));

    [Given("the session answers cost {int} cents for the plan, {int} cents for a build and {int} cents for a review")]
    public void GivenCosts(int plan, int build, int review)
    {
        _h.Builder.Answer(Phase.Plan, FakeAgent.Done() with { CostUsd = plan / 100m });
        _h.Builder.Answer(Phase.Build, FakeAgent.Done() with { CostUsd = build / 100m });
        _h.Reviewer.Answer(Phase.Review, FakeAgent.Done() with { CostUsd = review / 100m });
    }

    [When("the backend starts")]
    public Task WhenStarts() => _h.StartAsync(TestContext.Current.CancellationToken);

    [When("the builder answers a plan prompt")]
    public Task WhenPlan() => PromptAsync(Phase.Plan);

    [When("the builder answers a build prompt")]
    public Task WhenBuild() => PromptAsync(Phase.Build);

    [When("the reviewer answers a review prompt")]
    public Task WhenReview() => PromptAsync(Phase.Review);

    [When("the backend collects onto the branch {string}")]
    public Task WhenCollects(string branch) => _h.Backend.CollectAsync(branch, TestContext.Current.CancellationToken);

    [When("the backend ends")]
    public async Task WhenEnds() => await _h.Backend.DisposeAsync();

    [Then("the progress of the backend held the line {string} when the docker run started")]
    public void ThenLineBeforeRun(string line)
    {
        Assert.NotNull(_h.ProgressAtDockerRun);
        Assert.Contains(line, _h.ProgressAtDockerRun.Split(Environment.NewLine));
    }

    [Then("the docker run of the backend holds neither {string} nor {string}")]
    public void ThenRunLacks(string first, string second)
    {
        CommandSpec run = DockerRun();
        Assert.DoesNotContain(run.Arguments, argument => argument.Contains(first, StringComparison.Ordinal));
        Assert.DoesNotContain(run.Arguments, argument => argument.Contains(second, StringComparison.Ordinal));
    }

    [Then("the docker run of the backend mounts the clone at \\/work and names the image {string}")]
    public void ThenRunMounts(string image)
    {
        CommandSpec run = DockerRun();
        Assert.Contains($"{_h.CloneDirectory}:/work", run.Arguments);
        Assert.Contains(image, run.Arguments);
    }

    [Then("the sandbox of the backend needs a restore")]
    public void ThenNeedsRestore() => Assert.True(_h.Backend.Sandbox.NeedsRestore);

    [Then("git pushes the branch {string} before each of the {int} prompts")]
    public void ThenPushBeforeEach(string branch, int prompts)
    {
        string push = $"git push origin {branch}";
        List<string> steps = [.. _h.Timeline.Where(line => line == push || IsPrompt(line)).Select(line => line == push ? "push" : "prompt")];
        Assert.Equal([.. Enumerable.Repeat<string[]>(["push", "prompt"], prompts).SelectMany(pair => pair)], steps);
    }

    [Then("git pushes the branch {string} before the first session starts")]
    public void ThenPushBeforeSession(string branch)
    {
        int push = _h.Timeline.IndexOf($"git push origin {branch}");
        int start = _h.Timeline.IndexOf("POST /v1/sessions");
        Assert.True(push >= 0 && start > push, Commands);
    }

    [Then("git commits the work tree with the message {string} before the first push of the session branch")]
    public void ThenCommits(string message)
    {
        int commit = _h.Commands.FindIndex(c => c.Command is "git" && c.Arguments is ["commit", "-m", var text] && text == message);
        int push = _h.Commands.FindIndex(c => c.Command is "git" && c.Arguments is ["push", "origin", Branch]);
        Assert.True(commit >= 0 && push > commit, Commands);
    }

    [Then("git runs no commit before the first push of the session branch")]
    public void ThenNoCommit() => Assert.DoesNotContain(_h.Timeline.Take(FirstPush()), line => line.StartsWith("git commit", StringComparison.Ordinal));

    [Then("git pulls the session branch {int} times, after the plan prompt and after the build prompt")]
    public void ThenPulls(int count)
    {
        Assert.Equal(count, _h.Timeline.Count(IsPull));
        List<string> steps = [.. _h.Timeline.Where(line => IsPull(line) || IsPrompt(line)).Select(line => IsPull(line) ? "pull" : "prompt")];
        Assert.Equal(["prompt", "pull", "prompt", "pull", "prompt"], steps);
    }

    [Then("git reads the head of the session branch once before the build prompt and once after it, and at no other time")]
    public void ThenHeads()
    {
        List<string> steps =
        [
            .. _h.Timeline
                .Where(line => line.StartsWith("git ls-remote", StringComparison.Ordinal) || IsPrompt(line))
                .Select(line => IsPrompt(line) ? "prompt" : "head"),
        ];
        Assert.Equal(["prompt", "head", "prompt", "head", "prompt"], steps);
    }

    [Then("the answer of the build holds the fault {string}")]
    public void ThenFault(string fault) => Assert.Equal([fault], Assert.Single(_h.Answers).Faults);

    [Then("the answer of the build holds no fault")]
    public void ThenNoFault() => Assert.Empty(Assert.Single(_h.Answers).Faults);

    [Then("the prompt throws SessionBranchException with the text {string}")]
    public void ThenStops(string text) => Assert.Equal(text, Assert.IsType<SessionBranchException>(_stop).Message);

    [Then("no session starts")]
    public void ThenNoSession() => Assert.Empty(_h.Api.Sessions);

    [Then("the session of the {word} starts with a budget of {int} cents")]
    public void ThenBudget(string role, int cents) =>
        Assert.Equal(cents.ToString(CultureInfo.InvariantCulture), Assert.Single(_h.Api.Sessions, s => s.Role == role).BudgetCents);

    [Then("each session mounts the repository {string}")]
    public void ThenRepository(string url)
    {
        Assert.NotEmpty(_h.Api.Sessions);
        Assert.All(_h.Api.Sessions, s => Assert.Equal(url, s.RepositoryUrl));
    }

    [Then("the last two git commands check out {string} and pull the session branch into it")]
    public void ThenCollected(string branch)
    {
        List<string> git = Git;
        Assert.Equal($"git checkout {branch}", git[^2]);
        Assert.True(IsPull(git[^1]), Commands);
    }

    [Then("git deletes the session branch on origin as its last git command")]
    public void ThenDeletedLast()
    {
        string last = Git[^1];
        Assert.True(IsDelete(last) && last.EndsWith(" " + Branch, StringComparison.Ordinal), Commands);
    }

    [Then("the container of the backend is removed")]
    public void ThenRemoved() => Assert.Contains("docker rm", _h.Timeline);

    [Then("git pushes no session branch and deletes none")]
    public void ThenNoPush() => Assert.DoesNotContain(Git, line => line.StartsWith("git push", StringComparison.Ordinal));

    [Then("the sessions of the backend are {string} of the builder at {int} cents and {string} of the reviewer at {int} cents")]
    public void ThenSessions(string builder, int builderCents, string reviewer, int reviewerCents) =>
        Assert.Equal([new SessionCost(builder, "builder", builderCents), new SessionCost(reviewer, "reviewer", reviewerCents)], _h.Backend.Sessions);

    [Then("the backend reported {int} answers, of the costs {decimal} and {decimal} dollars")]
    public void ThenReported(int count, decimal first, decimal second)
    {
        Assert.Equal(count, _h.Reported.Count);
        Assert.Equal([first, second], _h.Reported.Select(r => r.Answer.CostUsd));
    }

    private async Task PromptAsync(Phase phase)
    {
        try
        {
            await _h.PromptAsync(phase, cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (SessionBranchException exception)
        {
            _stop = exception;
        }
    }
}
