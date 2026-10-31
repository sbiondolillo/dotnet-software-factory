using System.Text.Json;
using System.Text.Json.Nodes;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ManagedAgentsAgentSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly ManagedAgentsHarness _harness = new();
    private readonly CancellationTokenSource _cancel = new();
    private ManagedAgentsAgent? _agent;
    private ClaudeCodeResult? _result;
    private string _prompt = "";

    public void Dispose()
    {
        _cancel.Dispose();
        _harness.Dispose();
    }

    private static string Unescape(string text) => text.Replace("\\n", "\n", StringComparison.Ordinal);

    private async Task RunAsync(string prompt, string model, string phase = "agent")
    {
        _prompt = prompt;
        try
        {
            _result = await _agent!.RunAsync(prompt, model, 200, phase, _cancel.Token);
        }
        catch (ClaudeCodeException exception)
        {
            exceptionState.Exception = exception;
        }
        catch (OperationCanceledException exception)
        {
            exceptionState.Exception = exception;
        }
    }

    private string FirstMessage() => _harness.FirstMessage();

    private JsonNode SessionBody() => _harness.Posts("/v1/sessions").Single().Json!;

    private JsonArray Resources(string type) =>
        [.. SessionBody()["resources"]!.AsArray().Where(r => r!["type"]!.GetValue<string>() == type).Select(r => r!.DeepClone())];

    private static List<JsonNode> PostedEvents(ApiRequest request) => [.. request.Json!["events"]!.AsArray().Select(e => e!)];

    [Given("a stub Managed Agents API whose session id is {string} and whose repository resource id is {string}")]
    public void GivenApi(string session, string resource)
    {
        _harness.SessionId = session;
        _harness.ResourceId = resource;
    }

    [Given("the repo is {string}, the session branch is {string} and the base commit is {string}")]
    public void GivenRepo(string repo, string branch, string commit)
    {
        string[] parts = repo.Split('/');
        _harness.Owner = parts[0];
        _harness.Repo = parts[1];
        _harness.SessionBranch = branch;
        _harness.BaseCommit = commit;
    }

    [Given("a builder agent with the budget {int} cents")]
    public void GivenBuilder(int cents) => _agent = _harness.Build(SessionRole.Builder, cents);

    [Given("a reviewer agent with the budget {int} cents")]
    public void GivenReviewer(int cents) => _agent = _harness.Build(SessionRole.Reviewer, cents);

    [Given("the next turn holds the agent messages {string}")]
    [When("the next turn holds the agent messages {string}")]
    public void GivenMessage(string text) => _harness.Next.Messages.Add(Unescape(text));

    [Given("the next turn holds the agent messages {string} and {string}")]
    public void GivenMessages(string first, string second)
    {
        _harness.Next.Messages.Add(Unescape(first));
        _harness.Next.Messages.Add(Unescape(second));
    }

    [Given("the turn after it holds the agent messages {string}")]
    public void GivenAfterMessage(string text) => _harness.After.Messages.Add(Unescape(text));

    [Given("the turn after it holds {int} model requests")]
    public void GivenAfterRequests(int count) => _harness.After.ModelRequests = count;

    [Given("the turn after it ends with the session cost at {string} cents")]
    public void GivenAfterCost(string cents) => _harness.After.CostCents = cents;

    [Given("the event list answers {int} events a page")]
    public void GivenPageSize(int size) => _harness.PageSize = size;

    [Given("the next turn holds {int} model requests")]
    [When("the next turn holds {int} model requests")]
    public void GivenRequests(int count) => _harness.Next.ModelRequests = count;

    [Given("the next turn ends with the session cost at {string} cents")]
    [When("the next turn ends with the session cost at {string} cents")]
    public void GivenCost(string cents) => _harness.Next.CostCents = cents;

    [Given("the next turn ends with the stop reason {string}")]
    public void GivenStop(string reason) => _harness.Next.StopReason = reason;

    [When("the next message shows in the event list only after {int} reads, and its idle after {int} more")]
    public void GivenLag(int message, int idle)
    {
        _harness.Next.MessageLagReads = message;
        _harness.Next.IdleLagReads = idle;
    }

    [Given("the session lists the file {string} holding {string} created before the next message")]
    public void GivenOldFile(string name, string content) => _harness.AddFileNow(name, content);

    [Given("the next turn creates the file {string} holding {string} and then {string} holding {string}")]
    public void GivenTwoFiles(string first, string firstContent, string second, string secondContent)
    {
        _harness.Next.Files.Add(new ScriptedFile(first, firstContent));
        _harness.Next.Files.Add(new ScriptedFile(second, secondContent));
    }

    [Given("the next turn creates the file {string} holding {string}")]
    public void GivenFile(string name, string content) => _harness.Next.Files.Add(new ScriptedFile(name, content));

    [Given("the next turn creates the file {string} holding {string}, listed only after {int} reads of the file list")]
    public void GivenLateFile(string name, string content, int reads) => _harness.Next.Files.Add(new ScriptedFile(name, content, reads));

    [Given("the work tree file {string} holds {string}")]
    public void GivenWorkTreeFile(string path, string content)
    {
        string full = Path.Combine(_harness.WorkTree, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Given("the next turn holds an event with the authorization token {string} nested in an array of objects")]
    public void GivenTokenEvent(string token) =>
        _harness.Next.Events.Add(("agent.tool_use", new JsonObject
        {
            ["input"] = new JsonObject
            {
                ["resources"] = new JsonArray(new JsonObject { ["type"] = "github_repository", ["authorization_token"] = token }),
            },
        }));

    [Given("the next turn never goes idle")]
    public void GivenNoIdle() => _harness.Next.NeverIdle = true;

    [Given("the next turn never goes idle, and the caller cancels the token at the third read of the event list")]
    public void GivenNeverIdle()
    {
        _harness.Next.NeverIdle = true;
        _harness.OnEventsRead = read =>
        {
            if (read is 3)
            {
                _cancel.Cancel();
            }
        };
    }

    [When("the caller runs the prompt {string} with the model {string}")]
    public async Task WhenRuns(string prompt, string model) => await RunAsync(prompt, model);

    [When("the caller runs the prompt {string} with the phase {string} and the model {string}")]
    public async Task WhenRunsPhase(string prompt, string phase, string model) => await RunAsync(prompt, model, phase);

    [When("the caller runs a prompt of {int} letters {string} with the model {string}")]
    public async Task WhenRunsLong(int count, string letter, string model) =>
        await RunAsync(string.Concat(Enumerable.Repeat(letter, count)), model);

    [When("the next POST to {string} answers the status {int} once")]
    public void WhenFailsOnce(string path, int status) => _harness.FailOnce[("POST", path)] = status;

    [When("{int} minutes pass")]
    public async Task WhenMinutesPass(int minutes) => await _harness.Time.WaitAsync(TimeSpan.FromMinutes(minutes), CancellationToken.None);

    [When("the caller disposes the agent")]
    public async Task WhenDisposes() => await _agent!.DisposeAsync();

    [Then("the API got one POST to {string}")]
    public void ThenOnePost(string path) => Assert.Single(_harness.Posts(path));

    [Then("the API got no POST to {string}")]
    public void ThenNoPost(string path) => Assert.Empty(_harness.Posts(path));

    [Then("the session body names the agent {string} with the model {string} and the environment {string}")]
    public void ThenAgent(string agent, string model, string environment)
    {
        JsonNode body = SessionBody();
        Assert.Equal("agent_with_overrides", body["agent"]!["type"]!.GetValue<string>());
        Assert.Equal(agent, body["agent"]!["id"]!.GetValue<string>());
        Assert.Equal(model, body["agent"]!["model"]!["id"]!.GetValue<string>());
        Assert.Equal(environment, body["environment_id"]!.GetValue<string>());
    }

    [Then("the session body has one github_repository resource with the URL {string}, the mount path {string} and the first minted token")]
    public void ThenRepository(string url, string mount)
    {
        JsonNode resource = Assert.Single(Resources("github_repository"))!;
        Assert.Equal(url, resource["url"]!.GetValue<string>());
        Assert.Equal(mount, resource["mount_path"]!.GetValue<string>());
        Assert.Equal(_harness.Minted[0].Token, resource["authorization_token"]!.GetValue<string>());
    }

    [Then("the session body has the budget {string} in {string}")]
    public void ThenBudget(string amount, string currency)
    {
        JsonNode budget = SessionBody()["budget"]!;
        Assert.Equal("limit", budget["type"]!.GetValue<string>());
        Assert.Equal(amount, budget["max_list_cost"]!["amount"]!.GetValue<string>());
        Assert.Equal(currency, budget["max_list_cost"]!["currency"]!.GetValue<string>());
    }

    [Then("the session body has one initial user.message whose text holds {string}")]
    public void ThenInitial(string text)
    {
        JsonNode initial = Assert.Single(SessionBody()["initial_events"]!.AsArray())!;
        Assert.Equal("user.message", initial["type"]!.GetValue<string>());
        Assert.Contains(text, FirstMessage(), StringComparison.Ordinal);
    }

    [Then("the first token was minted with {word} access")]
    public void ThenMintAccess(string access) => Assert.Equal(access is "write", _harness.Minted[0].Write);

    [Then("every request to the API carries the header {string} with {string}, {string} with {string} and an {string} that holds {string}")]
    public void ThenHeaders(string keyName, string key, string versionName, string version, string betaName, string beta)
    {
        Assert.NotEmpty(_harness.Requests);
        Assert.All(_harness.Requests, request =>
        {
            Assert.Equal(key, request.Headers[keyName]);
            Assert.Equal(version, request.Headers[versionName]);
            Assert.Contains(beta, request.Headers[betaName], StringComparison.Ordinal);
        });
    }

    [Then("the API got one POST to {string} with one user.message whose text holds {string}")]
    public void ThenMessagePost(string path, string text)
    {
        JsonNode e = Assert.Single(PostedEvents(Assert.Single(_harness.Posts(path))));
        Assert.Equal("user.message", e["type"]!.GetValue<string>());
        Assert.Contains(text, e["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Then("the API got {int} POSTs to {string}, each with one user.message whose text holds {string}")]
    public void ThenMessagePosts(int count, string path, string text)
    {
        List<ApiRequest> posts = [.. _harness.Posts(path)];
        Assert.Equal(count, posts.Count);
        Assert.All(posts, post =>
        {
            JsonNode e = Assert.Single(PostedEvents(post));
            Assert.Equal("user.message", e["type"]!.GetValue<string>());
            Assert.Contains(text, e["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        });
    }

    [Then("the API got one POST to {string} with one user.interrupt")]
    public void ThenInterrupt(string path)
    {
        JsonNode e = Assert.Single(PostedEvents(Assert.Single(_harness.Posts(path))));
        Assert.Equal("user.interrupt", e["type"]!.GetValue<string>());
    }

    [Then("the first message holds these texts in this order:")]
    public void ThenInOrder(DataTable table)
    {
        string message = FirstMessage();
        int at = 0;
        foreach (DataTableRow row in table.Rows)
        {
            int found = message.IndexOf(row["text"], at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"The message holds no \"{row["text"]}\" after the index {at}:\n{message}");
            at = found + row["text"].Length;
        }
    }

    [Then("the first message holds {string}")]
    public void ThenHolds(string text) => Assert.Contains(text, FirstMessage(), StringComparison.Ordinal);

    [Then("the first message does not hold {string}")]
    public void ThenNotHolds(string text) => Assert.DoesNotContain(text, FirstMessage(), StringComparison.Ordinal);

    [Then("the first message holds the whole prompt")]
    public void ThenWhole() => Assert.Contains(_prompt, FirstMessage(), StringComparison.Ordinal);

    [Then("the answer text is {string}")]
    public void ThenText(string text) => Assert.Equal(text, _result!.Result);

    [Then("the answer text holds {string}")]
    public void ThenTextHolds(string text) => Assert.Contains(text, _result!.Result, StringComparison.Ordinal);

    [Then("the answer error flag is {word}")]
    public void ThenError(string flag) => Assert.Equal(flag is "true", _result!.IsError);

    [Then("the answer has the turn count {int}")]
    public void ThenTurns(int turns) => Assert.Equal(turns, _result!.NumTurns);

    [Then("the answer has the cost {double}")]
    public void ThenCost(double cost) => Assert.Equal((decimal)cost, _result!.CostUsd);

    [Then("Sessions holds one session {string} with the role {string} and {int} cents")]
    public void ThenSessions(string id, string role, int cents) =>
        Assert.Equal(new SessionCost(id, role, cents), Assert.Single(_agent!.Sessions));

    [Then("the agent waited {int} seconds between reads of the event list")]
    public void ThenWaited(int seconds)
    {
        List<TimeSpan> waits = [.. _harness.Waits.Where(w => w.After is not "/v1/files").Select(w => w.Span)];
        Assert.NotEmpty(waits);
        Assert.All(waits, span => Assert.Equal(TimeSpan.FromSeconds(seconds), span));
    }

    [Then("the agent waited no more than {int} seconds for the file list")]
    public void ThenFileWait(int seconds)
    {
        List<TimeSpan> waits = [.. _harness.Waits.Where(w => w.After is "/v1/files").Select(w => w.Span)];
        Assert.NotEmpty(waits);
        Assert.InRange(waits.Aggregate(TimeSpan.Zero, (sum, span) => sum + span), TimeSpan.FromSeconds(seconds - 5), TimeSpan.FromSeconds(seconds));
    }

    [Then("the work tree file {string} holds {string}")]
    public void ThenWorkTreeFile(string path, string content) =>
        Assert.Equal(content, File.ReadAllText(Path.Combine(_harness.WorkTree, path)));

    [Then("the work tree has no file {string}")]
    public void ThenNoWorkTreeFile(string path) => Assert.False(File.Exists(Path.Combine(_harness.WorkTree, path)));

    [Then("the API got one POST to {string} whose body holds {string}")]
    public void ThenUpload(string path, string text) =>
        Assert.Contains(text, Assert.Single(_harness.Posts(path)).Body, StringComparison.Ordinal);

    [Then("the session body has one file resource with the uploaded file id and the mount path {string}")]
    public void ThenFileResource(string mount)
    {
        JsonNode resource = Assert.Single(Resources("file"))!;
        Assert.Equal("file_up1", resource["file_id"]!.GetValue<string>());
        Assert.Equal(mount, resource["mount_path"]!.GetValue<string>());
    }

    [Then("the root file {string} holds every event of the session once, in the order the API processed them")]
    public void ThenEvents(string path)
    {
        string[] lines = File.ReadAllLines(Path.Combine(_harness.RootDirectory, path));
        List<string> ids = [.. lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("id").GetString()!;
        })];
        Assert.Equal([.. _harness.Events.Select(e => e["id"]!.GetValue<string>())], ids);
    }

    [Then("the root file {string} holds {string}")]
    public void ThenRootHolds(string path, string text) =>
        Assert.Contains(text, File.ReadAllText(Path.Combine(_harness.RootDirectory, path)), StringComparison.Ordinal);

    [Then("the root file {string} does not hold {string}")]
    public void ThenRootNotHolds(string path, string text) =>
        Assert.DoesNotContain(text, File.ReadAllText(Path.Combine(_harness.RootDirectory, path)), StringComparison.Ordinal);

    [Then("the API got one POST to {string} with the second minted token, before the POST to {string}")]
    public void ThenRotated(string resource, string events)
    {
        ApiRequest rotation = Assert.Single(_harness.Posts(resource));
        Assert.Equal(_harness.Minted[1].Token, rotation.Json!["authorization_token"]!.GetValue<string>());
        Assert.True(_harness.Requests.IndexOf(rotation) < _harness.Requests.IndexOf(_harness.Posts(events).First()));
    }

    [Then("the first minted token was revoked")]
    public void ThenRevoked() => Assert.Contains(_harness.Minted[0].Token, _harness.Revoked);

    [Then("the run throws OperationCanceledException")]
    public void ThenCancelled() => Assert.IsAssignableFrom<OperationCanceledException>(exceptionState.Exception);

    [Then("the log holds these lines in this order:")]
    public void ThenLog(DataTable table)
    {
        List<string> lines = [.. ConsoleHarness.Lines(_harness.Log)];
        int at = 0;
        foreach (DataTableRow row in table.Rows)
        {
            int found = lines.IndexOf(row["text"], at);
            Assert.True(found >= 0, $"The log holds no line \"{row["text"]}\" at or after the line {at}:\n{string.Join('\n', lines)}");
            at = found + 1;
        }
    }
}
