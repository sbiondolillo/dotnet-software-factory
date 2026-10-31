using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>One request the stub Managed Agents API got.</summary>
public sealed record ApiRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    public JsonNode? Json => Body is null || Path is "/v1/files" ? null : JsonNode.Parse(Body);
}

/// <summary>A file a turn writes to the outputs of the session.</summary>
public sealed record ScriptedFile(string Name, string Content, int ListLagReads = 0);

/// <summary>What the stub shows in the event list for the next message.</summary>
public sealed class ScriptedTurn
{
    public List<string> Messages { get; } = [];

    public int ModelRequests { get; set; } = 1;

    public string? CostCents { get; set; }

    public string StopReason { get; set; } = "end_turn";

    /// <summary>The reads of the event list, after the post, that do not show the message yet.</summary>
    public int MessageLagReads { get; set; }

    /// <summary>The reads of the event list, after the message shows, that do not show the idle yet.</summary>
    public int IdleLagReads { get; set; }

    public bool NeverIdle { get; set; }

    public List<ScriptedFile> Files { get; } = [];

    /// <summary>More events of the turn, after its agent messages. The stub adds the id, the type and the time.</summary>
    public List<(string Type, JsonObject Extra)> Events { get; } = [];
}

/// <summary>A <see cref="ManagedAgentsAgent"/> over a stub <see cref="HttpMessageHandler"/> that records each request and
/// answers the session, event, resource and file routes of the Managed Agents API with the shapes of 2026-10-30.</summary>
public sealed partial class ManagedAgentsHarness : IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-managed-agents-");
    private readonly List<JsonObject> _events = [];
    private readonly List<StoredFile> _files = [];
    private ScriptedTurn? _next;
    private ScriptedTurn? _after;
    private Pending? _pending;
    private int _seconds;
    private int _eventIds;
    private int _uploads;
    private int _fileListReads;
    private string _status = "idle";
    private string _cost = "0";
    private string? _lastPath;

    public ManagedAgentsHarness()
    {
        WorkTree = Path.Combine(_directory.FullName, "tree");
        RootDirectory = Path.Combine(_directory.FullName, "root");
        Directory.CreateDirectory(WorkTree);
        Directory.CreateDirectory(RootDirectory);
    }

    public string SessionId { get; set; } = "sesn_1";

    public string ResourceId { get; set; } = "sesrsc_1";

    public string Owner { get; set; } = "sbiondolillo";

    public string Repo { get; set; } = "factory-scratch";

    public string SessionBranch { get; set; } = "factory/7-session";

    public string BaseCommit { get; set; } = "abc1234";

    public string WorkTree { get; }

    public string RootDirectory { get; }

    public FakeTime Time { get; } = new();

    public StringWriter Log { get; } = new();

    public List<ApiRequest> Requests { get; } = [];

    public List<(string Token, bool Write)> Minted { get; } = [];

    public List<string> Revoked { get; } = [];

    /// <summary>Each wait of the agent, with the path of the request just before it.</summary>
    public List<(TimeSpan Span, string? After)> Waits { get; } = [];

    /// <summary>How many events one page of the event list holds.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Called with the number of each read of the event list.</summary>
    public Action<int>? OnEventsRead { get; set; }

    public int EventsReads { get; private set; }

    /// <summary>The turn the next message gets. A step that sets up a turn changes this one.</summary>
    public ScriptedTurn Next => _next ??= new ScriptedTurn();

    /// <summary>The turn the message after the next one gets. It becomes <see cref="Next"/> when the next message posts.</summary>
    public ScriptedTurn After => _after ??= new ScriptedTurn();

    /// <summary>The status that the next request of each method and path gets in place of its answer, once.</summary>
    public Dictionary<(string Method, string Path), int> FailOnce { get; } = [];

    /// <summary>Every event the event list holds, oldest first.</summary>
    public IReadOnlyList<JsonObject> Events => _events;

    public void Dispose()
    {
        Log.Dispose();
        _directory.Delete(recursive: true);
    }

    public ManagedAgentsAgent Build(SessionRole role, int budgetCents)
    {
        var http = new HttpClient(new Handler(this));
        var client = new ManagedAgentsClient(http, "sk-test", WaitAsync) { AgentId = "agent_test", EnvironmentId = "env_test" };
        var tokens = new GitHubAppTokens(new Handler(this), "1", "2", "unused", Time);
        var git = new Git(Mock.Of<CommandRunner>(MockBehavior.Strict));
        return new ManagedAgentsAgent(
            client, tokens, Owner, Repo, SessionBranch, BaseCommit, WorkTree, RootDirectory, role, budgetCents, Log, Time)
        {
            MintToken = (write, _) =>
            {
                string token = $"ghs_token_{Minted.Count + 1}";
                Minted.Add((token, write));
                return Task.FromResult(token);
            },
            RevokeToken = (token, _) =>
            {
                Revoked.Add(token);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>Adds a file to the outputs of the session now, before the next message.</summary>
    public void AddFileNow(string name, string content) => _files.Add(new StoredFile($"file_old{_files.Count}", name, Stamp(), content, 0));

    public IEnumerable<ApiRequest> Posts(string path) => Requests.Where(r => r.Method is "POST" && r.Path == path);

    /// <summary>The text of the first user.message of the session: the one initial event.</summary>
    public string FirstMessage() =>
        Posts("/v1/sessions").Single().Json!["initial_events"]![0]!["content"]![0]!["text"]!.GetValue<string>();

    private Task WaitAsync(TimeSpan span, CancellationToken cancellationToken)
    {
        Waits.Add((span, _lastPath));
        return Time.WaitAsync(span, cancellationToken);
    }

    private string Stamp() => Epoch.AddSeconds(++_seconds).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private JsonObject Event(string type, JsonObject? extra = null)
    {
        var e = new JsonObject { ["id"] = $"sevt_{++_eventIds:D3}", ["type"] = type, ["processed_at"] = Stamp() };
        foreach ((string key, JsonNode? value) in extra ?? [])
        {
            e[key] = value?.DeepClone();
        }

        return e;
    }

    private static JsonObject Text(string text) =>
        new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) };

    private void StartTurn(string text)
    {
        ScriptedTurn turn = Next;
        _next = _after;
        _after = null;
        _status = "running";
        _pending = new Pending(turn, text);
    }

    private void Advance()
    {
        if (_pending is not { } pending)
        {
            return;
        }

        pending.Reads++;
        ScriptedTurn turn = pending.Turn;
        if (!pending.Shown && pending.Reads > turn.MessageLagReads)
        {
            pending.Shown = true;
            _events.Add(Event("user.message", Text(pending.Text)));
            _events.Add(Event("session.status_running"));
            for (int i = 0; i < turn.ModelRequests; i++)
            {
                _events.Add(Event("span.model_request_start"));
                _events.Add(Event("span.model_request_end", new JsonObject { ["model_usage"] = new JsonObject { ["output_tokens"] = 10 } }));
            }

            foreach (string message in turn.Messages.Count > 0 ? turn.Messages : ["ok"])
            {
                _events.Add(Event("agent.message", Text(message)));
            }

            foreach ((string type, JsonObject extra) in turn.Events)
            {
                _events.Add(Event(type, extra));
            }
        }

        if (pending.Shown && !turn.NeverIdle && pending.Reads > turn.MessageLagReads + turn.IdleLagReads)
        {
            foreach (ScriptedFile file in turn.Files)
            {
                _files.Add(new StoredFile($"file_{_files.Count:D2}", file.Name, Stamp(), file.Content, _fileListReads + file.ListLagReads));
            }

            if (turn.CostCents is { } cost)
            {
                _cost = cost;
            }

            _events.Add(Event("session.usage", new JsonObject { ["usage"] = new JsonObject { ["list_cost"] = Cost() } }));
            _events.Add(Event("session.status_idle", new JsonObject { ["stop_reason"] = new JsonObject { ["type"] = turn.StopReason } }));
            _status = "idle";
            _pending = null;
        }
    }

    private JsonObject Cost() => new() { ["amount"] = _cost, ["currency"] = "USD" };

    private JsonObject Session() =>
        new()
        {
            ["id"] = SessionId,
            ["status"] = _status,
            ["resources"] = new JsonArray(new JsonObject { ["id"] = ResourceId, ["type"] = "github_repository" }),
            ["usage"] = new JsonObject { ["list_cost"] = Cost() },
        };

    private (int Status, string Body) EventsPage(string query)
    {
        OnEventsRead?.Invoke(++EventsReads);
        Advance();
        System.Text.RegularExpressions.Match page = PageCursor().Match(query);
        int offset = page.Success ? int.Parse(page.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        List<JsonObject> newest = [.. Enumerable.Reverse(_events)];
        var data = new JsonArray([.. newest.Skip(offset).Take(PageSize).Select(e => e.DeepClone())]);
        int next = offset + PageSize;
        return (200, new JsonObject { ["data"] = data, ["next_page"] = next < newest.Count ? $"page_{next}" : null }.ToJsonString());
    }

    private (int Status, string Body) FileList()
    {
        _fileListReads++;
        var data = new JsonArray([.. _files.Where(f => _fileListReads > f.VisibleAfter).Select(f => (JsonNode)new JsonObject
        {
            ["id"] = f.Id,
            ["filename"] = f.Name,
            ["size_bytes"] = Encoding.UTF8.GetByteCount(f.Content),
            ["created_at"] = f.CreatedAt,
        })]);
        return (200, new JsonObject { ["data"] = data, ["has_more"] = false }.ToJsonString());
    }

    private (int Status, string Body) Route(string method, string path, string query, string? body)
    {
        string session = $"/v1/sessions/{SessionId}";
        switch (method, path)
        {
            case ("POST", "/v1/sessions"):
                JsonNode created = JsonNode.Parse(body!)!;
                StartTurn(created["initial_events"]![0]!["content"]![0]!["text"]!.GetValue<string>());
                return (200, Session().ToJsonString());
            case ("GET", _) when path == session:
                return (200, Session().ToJsonString());
            case ("GET", _) when path == $"{session}/events":
                return EventsPage(query);
            case ("POST", _) when path == $"{session}/events":
                foreach (JsonNode? e in JsonNode.Parse(body!)!["events"]!.AsArray())
                {
                    switch (e!["type"]!.GetValue<string>())
                    {
                        case "user.message":
                            StartTurn(e["content"]![0]!["text"]!.GetValue<string>());
                            break;
                        case "user.interrupt":
                            _pending = null;
                            _status = "idle";
                            break;
                        default:
                            break;
                    }
                }

                return (200, "{}");
            case ("POST", _) when path == $"{session}/resources/{ResourceId}":
                return (200, new JsonObject { ["id"] = ResourceId, ["type"] = "github_repository" }.ToJsonString());
            case ("POST", "/v1/files"):
                string name = UploadName().Match(body!).Groups[1].Value;
                string id = $"file_up{++_uploads}";
                return (200, new JsonObject { ["id"] = id, ["filename"] = name }.ToJsonString());
            case ("GET", "/v1/files"):
                return FileList();
            case ("GET", _) when path.StartsWith("/v1/files/", StringComparison.Ordinal) && path.EndsWith("/content", StringComparison.Ordinal):
                string fileId = path["/v1/files/".Length..^"/content".Length];
                return _files.SingleOrDefault(f => f.Id == fileId) is { } file ? (200, file.Content) : (404, "{}");
            default:
                return (404, """{"type":"error","error":{"type":"not_found_error"}}""");
        }
    }

    [GeneratedRegex(@"[?&]page=page_([0-9]+)")]
    private static partial Regex PageCursor();

    [GeneratedRegex("filename=\"?([^\";\\r\\n]+)")]
    private static partial Regex UploadName();

    private sealed record StoredFile(string Id, string Name, string CreatedAt, string Content, int VisibleAfter);

    private sealed class Pending(ScriptedTurn turn, string text)
    {
        public ScriptedTurn Turn { get; } = turn;

        public string Text { get; } = text;

        public int Reads { get; set; }

        public bool Shown { get; set; }
    }

    private sealed class Handler(ManagedAgentsHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            Uri uri = request.RequestUri!;
            harness.Requests.Add(new ApiRequest(request.Method.Method, uri.AbsolutePath, uri.Query, headers, body));
            harness._lastPath = uri.AbsolutePath;
            (int status, string text) = uri.Host is not "api.anthropic.com"
                ? (404, "{}")
                : harness.FailOnce.Remove((request.Method.Method, uri.AbsolutePath), out int failure)
                    ? (failure, """{"type":"error","error":{"type":"rate_limit_error"}}""")
                    : harness.Route(request.Method.Method, uri.AbsolutePath, uri.Query, body);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
}
