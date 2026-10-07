using System.Globalization;
using System.Text;
using System.Text.Json;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeedbackRunSteps : IDisposable
{
    private readonly FactoryRunSteps _shared;

    private readonly FeedbackRunHarness _h = new();
    private readonly Dictionary<int, string?> _reports = [];
    private string _defaultReport = FeedbackRunHarness.FixedReport(FeedbackRunHarness.ThreadA, FeedbackRunHarness.ThreadB);
    private FeedbackRunResult? _result;
    private Exception? _exception;
    private SeenPrompt? _prompt;

    public FeedbackRunSteps(FactoryRunSteps shared, BuildPhaseSteps buildPhase, ReviewPromptState reviewPrompt)
    {
        _shared = shared;
        shared.OtherCommands = _h.Commands;
        shared.OtherPrompts = _h.Prompts;
        buildPhase.FirstPrompt = () => Build(1).Text;
        reviewPrompt.Lookup = () => _h.Prompts.FirstOrDefault(p => p.Kind is "review")?.Text;
    }

    /// <summary>The harness of the run, for the step class of a feature of the same run.</summary>
    public Specs.Unit.FeedbackRunHarness Harness => _h;

    public void Dispose() => _h.Dispose();

    private FeedbackRunResult Result => _result ?? throw new InvalidOperationException("the run gave no result");

    private SeenPrompt Build(int index) => _h.BuildPrompts.ElementAt(index - 1);

    private IEnumerable<(string Id, string Body)> Replies =>
        _h.Requests.Where(r => r.Content is not null && r.Content.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal)).Select(r =>
        {
            using JsonDocument json = JsonDocument.Parse(r.Content!);
            JsonElement variables = json.RootElement.GetProperty("variables");
            return (variables.GetProperty("threadId").GetString() ?? "", variables.GetProperty("body").GetString() ?? "");
        });

    private IEnumerable<string> Resolves => _h.Events.Where(e => e.StartsWith("resolve:", StringComparison.Ordinal)).Select(e => e["resolve:".Length..]);

    [Given("the repo {string} of the owner {string}, the pull request {int}, and an empty list of secret names")]
    public void GivenRepo(string repo, string owner, int pull)
    {
        _h.Repo = repo;
        _h.Owner = owner;
        _h.Pull = pull;
        _h.SecretNames = [];
    }

    [Given("the build uses the model {string} with the turn limit {int} and the review uses the model {string} with the turn limit {int}")]
    public void GivenModels(string build, int buildTurns, string review, int reviewTurns)
    {
        _h.BuildModel = build;
        _h.BuildTurns = buildTurns;
        _h.ReviewModel = review;
        _h.ReviewTurns = reviewTurns;
    }

    [Given("the author of each commit is {string} with the email {string}")]
    public void GivenAuthor(string name, string email)
    {
        _h.AuthorName = name;
        _h.AuthorEmail = email;
    }

    [Given("GitHub answers the read of the review threads of the pull request with the threads A and B")]
    public void GivenThreadsAb() => _h.Threads = [FeedbackRunHarness.ThreadA, FeedbackRunHarness.ThreadB];

    [Given("GitHub answers the read of the review threads of the pull request with the thread C alone")]
    public void GivenThreadC() => _h.Threads = [FeedbackRunHarness.ThreadC];

    [Given("GitHub answers the read of the review threads of the pull request with the threads A, B and C")]
    public void GivenThreadsAbc() => _h.Threads = [FeedbackRunHarness.ThreadA, FeedbackRunHarness.ThreadB, FeedbackRunHarness.ThreadC];

    [Given("GitHub answers the read of the pull request with the head ref {string}")]
    public void GivenHeadRef(string headRef) => _h.HeadRef = headRef;

    [Given("the head commit of the clone after the checkout is {string}")]
    public void GivenHead(string sha) => _h.HeadSha = sha;

    [Given("on a build prompt the agent writes the report that marks A and B fixed")]
    public void GivenFixedBoth() => _defaultReport = FeedbackRunHarness.FixedReport(FeedbackRunHarness.ThreadA, FeedbackRunHarness.ThreadB);

    [Given("on a build prompt the agent writes the report that marks A fixed and B disputed")]
    public void GivenFixedDisputed() => _defaultReport = FeedbackRunHarness.Items(
    [
        (FeedbackRunHarness.ThreadA, "fixed", FeedbackRunHarness.ReplyA),
        (FeedbackRunHarness.ThreadB, "disputed", FeedbackRunHarness.ReplyB),
    ]);

    [Given("on a build prompt the agent writes the report that marks A and B disputed")]
    public void GivenDisputedBoth() => _defaultReport = FeedbackRunHarness.Items(
    [
        (FeedbackRunHarness.ThreadA, "disputed", FeedbackRunHarness.ReplyA),
        (FeedbackRunHarness.ThreadB, "disputed", FeedbackRunHarness.ReplyB),
    ]);

    [Given("each dotnet command exits with the code 0 and the TRX file holds one passed test for each scenario")]
    public void GivenDotnet() => _shared.Harness.RedBuilds.Clear();

    [Given("on a review prompt the agent writes an approving review")]
    public void GivenApproves() => _shared.Harness.RejectedBuilds.Clear();

    [Given("GitHub answers each reply and each resolve with the status code 200 and no errors")]
    public void GivenReplies() => _h.ReplyErrorThread = null;

    [Given("GitHub answers the reply to the thread {string} with the status code 200 and a list errors")]
    public void GivenReplyErrors(string id) => _h.ReplyErrorThread = id;

    [Given("git reports no change in the work tree after the build")]
    public void GivenNoChange() => _h.ChangeAfterBuild = false;

    [Given("the caller gives the secret names {string} and {string}")]
    public void GivenSecrets(string first, string second) => _h.SecretNames = [first, second];

    [Given("on the first build prompt the agent writes no report")]
    public void GivenNoReport() => _reports[1] = null;

    [Given("on the first build prompt the agent writes the text {string} to the report")]
    public void GivenText(string text) => _reports[1] = text;

    [Given("on the first build prompt the agent writes a report with an item for the thread A alone")]
    public void GivenAlone() => _reports[1] = FeedbackRunHarness.FixedReport(FeedbackRunHarness.ThreadA);

    [Given("on the first build prompt the agent writes a report that gives the thread A the status {string}")]
    public void GivenStatus(string status) => _reports[1] = FeedbackRunHarness.Items(
    [
        (FeedbackRunHarness.ThreadA, status, FeedbackRunHarness.ReplyA),
        (FeedbackRunHarness.ThreadB, "fixed", FeedbackRunHarness.ReplyB),
    ]);

    [Given("on the first build prompt the agent writes a report that gives the thread A a reply of two spaces")]
    public void GivenBlank() => _reports[1] = FeedbackRunHarness.Items(
    [
        (FeedbackRunHarness.ThreadA, "fixed", "  "),
        (FeedbackRunHarness.ThreadB, "fixed", FeedbackRunHarness.ReplyB),
    ]);

    [Given("on the first build prompt the agent writes the report and on the second build prompt the agent writes no file")]
    public void GivenSecondNone() => _reports[2] = null;

    [Given("the test gate is red after the second build")]
    public void GivenRedSecond() => _shared.Harness.RedBuilds.Add(2);

    [Given("the review rejects the third build and approves the fourth build")]
    public void GivenRejectThird() => _shared.Harness.RejectedBuilds.Add(3);

    [When("a caller starts a feedback run")]
    public async Task WhenStarts()
    {
        FactoryRunHarness f = _shared.Harness;
        _h.RedBuilds.UnionWith(f.RedBuilds);
        _h.RejectedBuilds.UnionWith(f.RejectedBuilds);
        _h.TestStdout = f.TestStdout;
        _h.Labels.AddRange(f.Labels);
        _h.IssueBody = f.Body;
        _h.FirstBuildChanges.AddRange(f.FirstBuildChanges);
        _h.FirstBuildWrites.AddRange(f.FirstBuildWrites);
        _h.BaseOnlyTests.AddRange(f.BaseOnlyTests);
        foreach ((int build, (int Code, string Out, string Err) result) in f.CheckResults)
        {
            _h.CheckResults[build] = result;
        }

        _h.BuildIsError = f.BuildIsError;
        _h.Report = build => _reports.TryGetValue(build, out string? report) ? report : _defaultReport;
        try
        {
            _result = await _h.RunAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex is GitHubGraphQLException or GitHubRequestException or GitCommandException)
        {
            _exception = ex;
        }
    }

    [Then("FeedbackRun clones no repo and starts no container")]
    public void ThenNoClone()
    {
        Assert.DoesNotContain(_h.Commands, c => c.Command is "docker" || c.Arguments[0] is "clone");
    }

    [Then("FeedbackRun gives back a result with {int} thread(s) resolved, {int} thread(s) unresolved and {int} Redo(s)")]
    public void ThenResult(int resolved, int unresolved, int redos) =>
        Assert.Equal(new FeedbackRunResult(resolved, unresolved, redos), Result);

    [Then("FeedbackRun gives back a result with {int} Redo(s)")]
    public void ThenRedos(int redos) => Assert.Equal(redos, Result.Redos);

    [Then("FeedbackRun resolves no thread and gives back a result with {int} threads resolved, {int} threads unresolved and {int} Redos")]
    public void ThenNoResolve(int resolved, int unresolved, int redos)
    {
        Assert.Empty(Resolves);
        Assert.Equal(new FeedbackRunResult(resolved, unresolved, redos), Result);
    }

    [Then("git clones {string} into the directory that the caller gave and checks out the branch {string} in it")]
    public void ThenClones(string url, string branch)
    {
        CommandSpec clone = Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "clone");
        Assert.Equal([url, _h.CloneDirectory], clone.Arguments.Skip(1));
        CommandSpec checkout = Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "checkout");
        Assert.Equal(["checkout", branch], checkout.Arguments);
        Assert.Equal(_h.CloneDirectory, checkout.WorkingDirectory);
    }

    [Then("FeedbackRun runs one prompt with the model {string} and the turn limit {int}")]
    public void ThenPrompt(string model, int turns)
    {
        _prompt = Assert.Single(_h.Prompts, p => p.Model == model);
        Assert.Equal(turns.ToString(CultureInfo.InvariantCulture), _prompt.Turns);
    }

    [Then("that prompt holds {string}, {string}, {string}, {string}, {string}, {string}, {string}, {string} and {string}")]
    public void ThenHoldsNine(string a, string b, string c, string d, string e, string f, string g, string h, string i)
    {
        foreach (string text in new[] { a, b, c, d, e, f, g, h, i })
        {
            Assert.Contains(text, _prompt!.Text);
        }
    }

    [Then("that prompt holds {string}")]
    public void ThenHolds(string text) => Assert.Contains(text, _prompt!.Text);

    [Then("FeedbackRun adds a reply to the thread A and a reply to the thread B")]
    public void ThenRepliesAb() =>
        Assert.Equal([FeedbackRunHarness.ThreadA, FeedbackRunHarness.ThreadB], Replies.Select(r => r.Id));

    [Then("FeedbackRun adds {int} replies in the run")]
    public void ThenReplyCount(int count) => Assert.Equal(count, Replies.Count());

    [Then("FeedbackRun adds no reply")]
    public void ThenNoReply() => Assert.Empty(Replies);

    [Then("the first prompt of the run has the model {string}")]
    public void ThenFirstPrompt(string model) => Assert.Equal(model, _h.Prompts[0].Model);

    [Then("FeedbackRun runs a review prompt that holds {string}")]
    public void ThenReviewHolds(string text) => Assert.Contains(text, Assert.Single(_h.Prompts, p => p.Kind is "review").Text);

    [Then("docker gives both names {string} and {string} to the command {string}")]
    public void ThenNames(string first, string second, string command)
    {
        foreach (CommandSpec spec in _h.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(FeedbackRunHarness.ContainerId);
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

    [Then("git pushes the branch {string} before FeedbackRun adds the first reply")]
    public void ThenPushBefore(string branch)
    {
        CommandSpec spec = Assert.Single(_h.Commands, c => c.Command is "git" && c.Arguments[0] is "push");
        Assert.Equal(["push", "origin", branch], spec.Arguments);
        int push = _h.Events.IndexOf("push");
        int reply = _h.Events.FindIndex(e => e.StartsWith("reply:", StringComparison.Ordinal));
        Assert.True(push >= 0);
        Assert.True(reply > push);
    }

    [Then("FeedbackRun adds the reply {string} to the thread {string} and resolves the thread {string}")]
    public void ThenReplyResolve(string reply, string thread, string resolved)
    {
        Assert.Contains((thread, reply), Replies);
        Assert.Contains(resolved, Resolves);
    }

    [Then("FeedbackRun adds the reply {string} to the thread {string}, and resolves the thread {string} alone")]
    public void ThenReplyResolveAlone(string reply, string thread, string resolved)
    {
        Assert.Contains((thread, reply), Replies);
        Assert.Equal([resolved], Resolves);
    }

    [Then("FeedbackRun runs the second build prompt before any dotnet command")]
    public void ThenSecondBeforeDotnet()
    {
        int second = _h.Events.Select((e, i) => (e, i)).Where(x => x.e is "build").Skip(1).First().i;
        int dotnet = _h.Events.IndexOf("dotnet");
        Assert.True(dotnet < 0 || second < dotnet);
        Assert.True(dotnet >= 0);
    }

    [Then("the second build prompt holds {string}")]
    public void ThenSecond(string text) => Assert.Contains(text, Build(2).Text);

    [Then("the second build prompt holds {string} and {string}")]
    public void ThenSecondTwo(string first, string second)
    {
        Assert.Contains(first, Build(2).Text);
        Assert.Contains(second, Build(2).Text);
    }

    [Then("the third build prompt holds {string}")]
    public void ThenThird(string text) => Assert.Contains(text, Build(3).Text);

    [Then("FeedbackRun runs the second build prompt before any prompt with the model {string}")]
    public void ThenSecondBeforeReview(string model)
    {
        int second = _h.Prompts.FindIndex(p => p.Kind is "build" && p.BuildsBefore is 2);
        int review = _h.Prompts.FindIndex(p => p.Model == model);
        Assert.True(second >= 0);
        Assert.True(review > second);
    }

    [Then("FeedbackRun runs {int} build prompts in the run")]
    public void ThenBuildCount(int count) => Assert.Equal(count, _h.BuildPrompts.Count());

    [Then("FeedbackRun posts one comment on the pull request {int} of {string} and the comment holds {string}")]
    public void ThenCommentOf(int number, string slug, string text)
    {
        SeenRequest request = Assert.Single(_h.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal));
        Assert.Contains($"/repos/{slug}/issues/{number}/comments", request.Uri);
        Assert.Contains(text, Assert.Single(_h.CommentBodies));
    }

    [Then("FeedbackRun posts one comment on the pull request {int} that holds {string}")]
    public void ThenComment(int number, string text)
    {
        SeenRequest request = Assert.Single(_h.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal));
        Assert.Contains($"/issues/{number}/comments", request.Uri);
        Assert.Contains(text, Assert.Single(_h.CommentBodies));
    }

    [Then("FeedbackRun posts one comment of {int} bytes or fewer in bytes, and the comment holds the letter {string} {int} times in sequence and {string}")]
    public void ThenCut(int limit, string letter, int count, string marker)
    {
        string body = Assert.Single(_h.CommentBodies);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= limit);
        Assert.Contains(string.Concat(Enumerable.Repeat(letter, count)), body);
        Assert.Contains(marker, body);
    }

    [Then("FeedbackRun removes the container after the last reply")]
    public void ThenRemovedAfter()
    {
        int rm = _h.Events.IndexOf("rm");
        Assert.True(rm > _h.Events.FindLastIndex(e => e.StartsWith("reply:", StringComparison.Ordinal)));
        Assert.True(rm >= 0);
    }

    [Then("FeedbackRun throws GitHubGraphQLException")]
    public void ThenThrows() => Assert.IsType<GitHubGraphQLException>(_exception);

    [Then("FeedbackRun removes the container")]
    public void ThenRemoved() => Assert.Contains("rm", _h.Events);
}
