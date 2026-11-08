using System.Text.Json;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeedbackRunSteps(FactoryRunSteps shared, BuildPhaseSteps buildPhase, ReviewPromptState reviewPrompt)
{
    private RunResult<Answered>? _result;
    private Exception? _exception;
    private AgentRequest? _prompt;

    /// <summary>The harness of the run, which the step classes of a factory run share.</summary>
    public RunHarness Harness => shared.Harness;

    private RunHarness H => shared.Harness;

    private RunResult<Answered> Result => _result ?? throw new InvalidOperationException("the run gave no result");

    private string Build(int index) => H.Builder.Of(Phase.Build).ElementAt(index - 1).Prompt.Text;

    private IEnumerable<(string Id, string Body)> Replies =>
        H.Requests.Where(r => r.Content is not null && r.Content.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal)).Select(r =>
        {
            using var json = JsonDocument.Parse(r.Content!);
            JsonElement variables = json.RootElement.GetProperty("variables");
            return (variables.GetProperty("threadId").GetString() ?? "", variables.GetProperty("body").GetString() ?? "");
        });

    private IEnumerable<string> Resolves => H.Events.Where(e => e.StartsWith("resolve:", StringComparison.Ordinal)).Select(e => e["resolve:".Length..]);

    [Given("the repo {string} of the owner {string}, the pull request {int}, and an empty list of secret names")]
    public void GivenRepo(string repo, string owner, int pull)
    {
        H.Repo = repo;
        H.Owner = owner;
        H.Pull = pull;
        H.SecretNames = [];
    }

    [Given("the build uses the model {string} with the turn limit {int} and the review uses the model {string} with the turn limit {int}")]
    public void GivenModels(string build, int buildTurns, string review, int reviewTurns)
    {
        H.BuildModel = build;
        H.BuildTurns = buildTurns;
        H.ReviewModel = review;
        H.ReviewTurns = reviewTurns;
    }

    [Given("the author of each commit is {string} with the email {string}")]
    public void GivenAuthor(string name, string email)
    {
        H.AuthorName = name;
        H.AuthorEmail = email;
    }

    [Given("GitHub answers the read of the review threads of the pull request with the threads A and B")]
    public void GivenThreadsAb() => H.Threads = [RunHarness.ThreadA, RunHarness.ThreadB];

    [Given("GitHub answers the read of the review threads of the pull request with the thread C alone")]
    public void GivenThreadC() => H.Threads = [RunHarness.ThreadC];

    [Given("GitHub answers the read of the review threads of the pull request with the threads A, B and C")]
    public void GivenThreadsAbc() => H.Threads = [RunHarness.ThreadA, RunHarness.ThreadB, RunHarness.ThreadC];

    [Given("GitHub answers the read of the pull request with the head ref {string}")]
    public void GivenHeadRef(string headRef) => H.HeadRef = headRef;

    [Given("the head commit of the clone after the checkout is {string}")]
    public void GivenHead(string sha) => H.HeadSha = sha;

    [Given("on a build prompt the agent writes the report that marks A and B fixed")]
    public void GivenFixedBoth() => H.Report(RunHarness.FixedReport(RunHarness.ThreadA, RunHarness.ThreadB));

    [Given("on a build prompt the agent writes the report that marks A fixed and B disputed")]
    public void GivenFixedDisputed() => H.Report(RunHarness.Items(
    [
        (RunHarness.ThreadA, "fixed", RunHarness.ReplyA),
        (RunHarness.ThreadB, "disputed", RunHarness.ReplyB),
    ]));

    [Given("on a build prompt the agent writes the report that marks A and B disputed")]
    public void GivenDisputedBoth() => H.Report(RunHarness.Items(
    [
        (RunHarness.ThreadA, "disputed", RunHarness.ReplyA),
        (RunHarness.ThreadB, "disputed", RunHarness.ReplyB),
    ]));

    [Given("each dotnet command exits with the code 0 and the TRX file holds one passed test for each scenario")]
    public void GivenDotnet() => H.RedBuilds.Clear();

    /// <summary>The reviewer approves every review by default, so the step sets nothing.</summary>
    [Given("on a review prompt the agent writes an approving review")]
    public static void GivenApproves()
    {
    }

    [Given("GitHub answers each reply and each resolve with the status code 200 and no errors")]
    public void GivenReplies() => H.ReplyErrorThread = null;

    [Given("GitHub answers the reply to the thread {string} with the status code 200 and a list errors")]
    public void GivenReplyErrors(string id) => H.ReplyErrorThread = id;

    [Given("git reports no change in the work tree after the build")]
    public void GivenNoChange() => H.ChangeAfterBuild = false;

    [Given("the caller gives the secret names {string} and {string}")]
    public void GivenSecrets(string first, string second) => H.SecretNames = [first, second];

    [Given("on the first build prompt the agent writes no report")]
    public void GivenNoReport() => H.Report(null, 1);

    [Given("on the first build prompt the agent writes the text {string} to the report")]
    public void GivenText(string text) => H.Report(text, 1);

    [Given("on the first build prompt the agent writes a report with an item for the thread A alone")]
    public void GivenAlone() => H.Report(RunHarness.FixedReport(RunHarness.ThreadA), 1);

    [Given("on the first build prompt the agent writes a report that gives the thread A the status {string}")]
    public void GivenStatus(string status) => H.Report(
        RunHarness.Items(
        [
            (RunHarness.ThreadA, status, RunHarness.ReplyA),
            (RunHarness.ThreadB, "fixed", RunHarness.ReplyB),
        ]),
        1);

    [Given("on the first build prompt the agent writes a report that gives the thread A a reply of two spaces")]
    public void GivenBlank() => H.Report(
        RunHarness.Items(
        [
            (RunHarness.ThreadA, "fixed", "  "),
            (RunHarness.ThreadB, "fixed", RunHarness.ReplyB),
        ]),
        1);

    [Given("on the first build prompt the agent writes the report and on the second build prompt the agent writes no file")]
    public void GivenSecondNone() => H.Report(null, 2);

    [When("a caller starts a feedback run")]
    public async Task WhenStarts()
    {
        buildPhase.FirstPrompt = () => Build(1);
        reviewPrompt.Lookup = () => H.Reviewer.Of(Phase.Review).FirstOrDefault()?.Prompt.Text;
        try
        {
            _result = await H.RunFeedbackAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex is GitHubGraphQLException or GitHubRequestException or GitCommandException or ClaudeCodeException)
        {
            _exception = ex;
        }
    }

    [Then("FeedbackRun clones no repo and starts no container")]
    public void ThenNoClone() =>
        Assert.DoesNotContain(H.Commands, c => c.Command is "docker" || c.Arguments[0] is "clone");

    [Then("FeedbackRun gives back a result with {int} thread(s) resolved, {int} thread(s) unresolved and {int} Redo(s)")]
    public void ThenResult(int resolved, int unresolved, int redos)
    {
        Assert.Equal(redos, Result.Redos);
        if (Result.Passed is Answered answered)
        {
            Assert.Equal((resolved, unresolved), (answered.Resolved, answered.Unresolved));
            return;
        }

        // A round with no pass resolves no thread, so each unresolved thread that GitHub listed stays unresolved.
        Assert.NotNull(Result.Stop);
        Assert.Equal(0, resolved);
        Assert.Empty(Resolves);
        Assert.Equal(unresolved, H.Threads.Count(id => id is not RunHarness.ThreadC));
    }

    [Then("FeedbackRun resolves the threads {string} and {string}")]
    public void ThenResolvesBoth(string first, string second) => Assert.Equal([first, second], Resolves);

    [Then("FeedbackRun gives back a result with {int} Redo(s)")]
    public void ThenRedos(int redos) => Assert.Equal(redos, Result.Redos);

    [Then("git clones {string} into the directory that the caller gave and checks out the branch {string} in it")]
    public void ThenClones(string url, string branch)
    {
        CommandSpec clone = Assert.Single(H.Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
        Assert.Equal([url, H.CloneDirectory], clone.Arguments.Skip(1));
        CommandSpec checkout = Assert.Single(H.Commands, c => c.Command is "git" && c.Arguments[0] is "checkout");
        Assert.Equal(["checkout", branch], checkout.Arguments);
        Assert.Equal(H.CloneDirectory, checkout.WorkingDirectory);
    }

    [Then("FeedbackRun runs one prompt with the model {string} and the turn limit {int}")]
    public void ThenPrompt(string model, int turns)
    {
        _prompt = Assert.Single(H.AgentRequests, r => r.Model == model);
        Assert.Equal(turns, _prompt.MaxTurns);
    }

    [Then("that prompt holds {string}, {string}, {string}, {string}, {string}, {string}, {string}, {string} and {string}")]
    public void ThenHoldsNine(string a, string b, string c, string d, string e, string f, string g, string h, string i)
    {
        foreach (string text in new[] { a, b, c, d, e, f, g, h, i })
        {
            Assert.Contains(text, _prompt!.Prompt.Text, StringComparison.Ordinal);
        }
    }

    [Then("that prompt holds {string}")]
    public void ThenHolds(string text) => Assert.Contains(text, _prompt!.Prompt.Text, StringComparison.Ordinal);

    [Then("FeedbackRun adds a reply to the thread A and a reply to the thread B")]
    public void ThenRepliesAb() =>
        Assert.Equal([RunHarness.ThreadA, RunHarness.ThreadB], Replies.Select(r => r.Id));

    [Then("FeedbackRun adds {int} replies in the run")]
    public void ThenReplyCount(int count) => Assert.Equal(count, Replies.Count());

    [Then("FeedbackRun adds no reply")]
    public void ThenNoReply() => Assert.Empty(Replies);

    [Then("the first prompt of the run has the model {string}")]
    public void ThenFirstPrompt(string model) => Assert.Equal(model, H.AgentRequests[0].Model);

    [Then("FeedbackRun runs a review prompt that holds {string}")]
    public void ThenReviewHolds(string text) => Assert.Contains(text, Assert.Single(H.Reviewer.Of(Phase.Review)).Prompt.Text, StringComparison.Ordinal);

    [Then("docker gives both names {string} and {string} to the command {string}")]
    public void ThenNames(string first, string second, string command)
    {
        foreach (CommandSpec spec in H.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
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

        Assert.Contains(H.Commands, c => c.Arguments.Contains("--project") && c.Arguments.Contains("tests/Integration"));
    }

    [Then("git pushes the head branch {string} once, before FeedbackRun adds the first reply")]
    public void ThenHeadPushBefore(string branch)
    {
        List<CommandSpec> pushes = [.. H.Commands.Where(c => c.Command is "git" && c.Arguments[0] is "push")];
        int head = Assert.Single(Enumerable.Range(0, pushes.Count), i => pushes[i].Arguments is ["push", "origin", var name] && name == branch);
        List<int> pushEvents = [.. Enumerable.Range(0, H.Events.Count).Where(i => H.Events[i] is "push")];
        int reply = H.Events.FindIndex(e => e.StartsWith("reply:", StringComparison.Ordinal));
        Assert.True(reply > pushEvents[head]);
    }

    [Then("git pushes the branch {string} before FeedbackRun adds the first reply")]
    public void ThenPushBefore(string branch)
    {
        CommandSpec spec = Assert.Single(H.Commands, c => c.Command is "git" && c.Arguments[0] is "push");
        Assert.Equal(["push", "origin", branch], spec.Arguments);
        int push = H.Events.IndexOf("push");
        int reply = H.Events.FindIndex(e => e.StartsWith("reply:", StringComparison.Ordinal));
        Assert.True(push >= 0);
        Assert.True(reply > push);
    }

    [Then("FeedbackRun adds the reply {string} to the thread {string} and resolves the thread {string}")]
    public void ThenReplyResolve(string reply, string thread, string resolved)
    {
        Assert.Contains((thread, reply), Replies);
        Assert.Contains(resolved, Resolves);
    }

    [Then("FeedbackRun runs the second build prompt before any dotnet command")]
    public void ThenSecondBeforeDotnet()
    {
        int second = H.Events.Select((e, i) => (e, i)).Where(x => x.e is "build").Skip(1).First().i;
        int dotnet = H.Events.IndexOf("dotnet");
        Assert.True(dotnet < 0 || second < dotnet);
        Assert.True(dotnet >= 0);
    }

    [Then("the second build prompt holds {string}")]
    public void ThenSecond(string text) => Assert.Contains(text, Build(2), StringComparison.Ordinal);

    [Then("the second build prompt holds {string} and {string}")]
    public void ThenSecondTwo(string first, string second)
    {
        Assert.Contains(first, Build(2), StringComparison.Ordinal);
        Assert.Contains(second, Build(2), StringComparison.Ordinal);
    }

    [Then("the third build prompt holds {string}")]
    public void ThenThird(string text) => Assert.Contains(text, Build(3), StringComparison.Ordinal);

    [Then("FeedbackRun runs {int} build prompts in the run")]
    public void ThenBuildCount(int count) => Assert.Equal(count, H.Builds);

    [Then("FeedbackRun posts one comment on the pull request {int} of {string} and the comment holds {string}")]
    public void ThenCommentOf(int number, string slug, string text)
    {
        SeenRequest request = Assert.Single(H.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal));
        Assert.Contains($"/repos/{slug}/issues/{number}/comments", request.Uri, StringComparison.Ordinal);
        Assert.Contains(text, Assert.Single(H.CommentBodies), StringComparison.Ordinal);
    }

    [Then("FeedbackRun posts one comment on the pull request {int} that holds {string}")]
    public void ThenComment(int number, string text)
    {
        SeenRequest request = Assert.Single(H.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal));
        Assert.Contains($"/issues/{number}/comments", request.Uri, StringComparison.Ordinal);
        Assert.Contains(text, Assert.Single(H.CommentBodies), StringComparison.Ordinal);
    }

    [Then("FeedbackRun removes the container after the last reply")]
    public void ThenRemovedAfter()
    {
        int rm = H.Events.IndexOf("rm");
        Assert.True(rm > H.Events.FindLastIndex(e => e.StartsWith("reply:", StringComparison.Ordinal)));
        Assert.True(rm >= 0);
    }

    [Then("FeedbackRun resolves no thread")]
    public void ThenNoResolveAtAll() => Assert.Empty(Resolves);

    [Then("FeedbackRun throws GitHubGraphQLException")]
    public void ThenThrows() => Assert.IsType<GitHubGraphQLException>(_exception);

    [Then("the job summary of the round is one section {string} with the rows {string}, {string}, {string} and {string}, in this order")]
    public void ThenSummarySection(string heading, string first, string second, string third, string fourth)
    {
        string[] lines = H.Summary.Split('\n');
        Assert.Equal(heading, lines[0]);
        Assert.Single(lines, line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal([first, second, third, fourth], lines.Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Skip(1).Select(line => line.Split('|')[1].Trim()));
    }

    [Then("FeedbackRun removes the container")]
    public void ThenRemoved() => Assert.Contains("rm", H.Events);
}
