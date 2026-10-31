using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Specs.Unit;

/// <summary>What the fake session answers to one <c>user.message</c>: the answer text, the error flag, the cost and whether the turn pushes.</summary>
public sealed record ManagedTurn(string Result, bool IsError = false, int Cents = 0, bool Pushes = true);

/// <summary>One session of the fake: its id, its model, its events and its cost so far in cents.</summary>
public sealed class FakeSession(string id, string model)
{
    public string Id { get; } = id;

    public string Model { get; } = model;

    public List<JsonObject> Events { get; } = [];

    public int Cents { get; set; }

    public List<string> Texts { get; } = [];
}

/// <summary>
/// A stub of the Managed Agents API and of the GitHub App token routes, for the run harnesses. Each <c>user.message</c> calls
/// <see cref="OnMessage"/>, which plays the agent: it writes the files of the turn through <see cref="Stage"/> and <see cref="AddOutput"/>.
/// A staged file reaches the clone at the next <c>git pull</c>, and an output file is in the files list of the session.
/// </summary>
public sealed class ManagedRunFake
{
    public const string ApiKey = "secret-key";
    public const string AgentId = "agent_011dsfbuilder";
    public const string EnvironmentId = "env_011dsfbuilder";
    public const string AppId = "350419";
    public const string InstallationId = "84112233";
    public const string AppToken = "ghs_fakeinstallationtoken";

    private readonly Dictionary<string, string?> _pending = [];
    private readonly List<(string Id, string Session, string Name, string Content, DateTimeOffset Created)> _files = [];
    private int _events;
    private int _heads;
    private FakeSession? _current;

    /// <summary>A private key in PEM form that the token mint signs with.</summary>
    public static string PrivateKeyPem { get; } = CreateKey();

    /// <summary>Plays the agent for one message: the text and the model of the session.</summary>
    public Func<string, string, ManagedTurn> OnMessage { get; set; } = (_, _) => new ManagedTurn("done");

    /// <summary>The sessions in the order of their creation.</summary>
    public List<FakeSession> Sessions { get; } = [];

    /// <summary>The tracked files under <c>.factory/</c> that <c>git ls-files</c> lists after a pull.</summary>
    public List<string> TrackedFactoryFiles { get; } = [];

    /// <summary>The number of <c>git pull</c> commands so far.</summary>
    public int Pulls { get; private set; }

    /// <summary>The environment of a console program on the managed path, besides the token and the key.</summary>
    public static Dictionary<string, string> Environment() => new()
    {
        ["FACTORY_AGENT_ID"] = AgentId,
        ["FACTORY_ENVIRONMENT_ID"] = EnvironmentId,
        ["FACTORY_APP_ID"] = AppId,
        ["FACTORY_APP_INSTALLATION_ID"] = InstallationId,
        ["FACTORY_APP_PRIVATE_KEY"] = PrivateKeyPem,
    };

    /// <summary>The settings of a run on the managed path.</summary>
    public static Core.ManagedAgentsSettings Settings() => new(ApiKey, AgentId, EnvironmentId, AppId, InstallationId, PrivateKeyPem);

    /// <summary>True for a request that this fake answers.</summary>
    public static bool Handles(Uri uri) =>
        uri.Host is "api.anthropic.com"
        || uri.AbsolutePath.StartsWith("/app/installations/", StringComparison.Ordinal)
        || uri.AbsolutePath is "/installation/token";

    /// <summary>Stages a write of the session to a tracked file, or a removal for null. The next pull applies it.</summary>
    public void Stage(string relative, string? text) => _pending[relative] = text;

    /// <summary>Adds an output file of the current session, as the session writes it to its outputs directory.</summary>
    public void AddOutput(string relative, string text)
    {
        if (_current is not FakeSession session)
        {
            return;
        }

        _files.Add(($"file_{_files.Count + 1:D4}", session.Id, Path.GetFileName(relative), text, DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    /// <summary>Applies each staged write to <paramref name="clone"/>, as a fast-forward from the session branch does.</summary>
    public void Pull(string clone)
    {
        Pulls++;
        foreach ((string relative, string? text) in _pending)
        {
            string full = Path.Combine(clone, relative);
            if (text is null)
            {
                File.Delete(full);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        _pending.Clear();
    }

    /// <summary>The output of <c>git ls-remote</c> for the branch the last argument names: a commit that moves with each push of a session.</summary>
    public string LsRemote(IReadOnlyList<string> arguments)
    {
        string branch = arguments[^1];
        string name = branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
        return $"{Head()}\t{name}\n";
    }

    /// <summary>Answers one request: the status code and the body.</summary>
    public (int Status, string Body) Route(string method, Uri uri, string? content)
    {
        string path = uri.AbsolutePath;
        if (path.StartsWith("/app/installations/", StringComparison.Ordinal))
        {
            return (201, JsonSerializer.Serialize(new { token = AppToken, expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture) }));
        }

        if (path is "/installation/token")
        {
            return (204, "");
        }

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return (method, parts) switch
        {
            ("POST", ["v1", "sessions"]) => Create(content ?? "{}"),
            ("POST", ["v1", "sessions", string id, "events"]) => Post(id, content ?? "{}"),
            ("GET", ["v1", "sessions", string id, "events"]) => EventsOf(id, uri.Query),
            ("GET", ["v1", "sessions", string id]) => (200, SessionJson(Find(id), "idle")),
            ("POST", ["v1", "sessions", _, "resources", _]) => (200, "{}"),
            ("POST", ["v1", "sessions", _, "archive"]) => (200, "{}"),
            ("GET", ["v1", "files"]) => FilesOf(uri.Query),
            ("GET", ["v1", "files", string id, "content"]) => (200, _files.First(f => f.Id == id).Content),
            ("POST", ["v1", "files"]) => (200, JsonSerializer.Serialize(new { id = $"file_up{_files.Count + 1:D4}", type = "file", filename = "upload" })),
            _ => (404, "{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\"}}"),
        };
    }

    private static string CreateKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private string Head() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"head {_heads}")))[..40].ToLowerInvariant();

    private FakeSession Find(string id) => Sessions.First(s => s.Id == id);

    private (int Status, string Body) Create(string content)
    {
        JsonNode body = JsonNode.Parse(content)!;
        string model = body["agent"]?["model"]?["id"]?.GetValue<string>() ?? body["agent"]?["model"]?.ToString() ?? "";
        var session = new FakeSession($"sesn_{Sessions.Count + 1:D2}", model);
        Sessions.Add(session);
        string answer = SessionJson(session, "running");
        foreach (JsonNode? item in body["initial_events"]?.AsArray() ?? [])
        {
            Message(session, item!);
        }

        return (200, answer);
    }

    private (int Status, string Body) Post(string id, string content)
    {
        FakeSession session = Find(id);
        JsonArray echoed = [];
        foreach (JsonNode? item in JsonNode.Parse(content)!["events"]?.AsArray() ?? [])
        {
            echoed.Add(Message(session, item!));
        }

        return (200, new JsonObject { ["data"] = echoed }.ToJsonString());
    }

    private JsonObject Message(FakeSession session, JsonNode item)
    {
        string type = item["type"]?.GetValue<string>() ?? "";
        JsonObject posted = Event(type);
        if (type is not "user.message")
        {
            session.Events.Add(posted);
            return (JsonObject)posted.DeepClone();
        }

        string text = string.Concat((item["content"]?.AsArray() ?? []).Select(part => part?["text"]?.GetValue<string>() ?? ""));
        session.Texts.Add(text);
        posted["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
        session.Events.Add(posted);
        _current = session;
        int outputsBefore = _files.Count;
        ManagedTurn turn = OnMessage(text, session.Model);
        _current = null;
        if (turn.Pushes && !turn.IsError)
        {
            _heads++;
        }

        session.Cents += turn.Cents;
        string[] outputs = [.. _files.Skip(outputsBefore).Select(f => f.Name)];
        string answer = outputs.Length is 0 ? turn.Result : $"{turn.Result}\n\noutputs: {string.Join(", ", outputs)}";
        session.Events.Add(Event("span.model_request_start"));
        JsonObject end = Event("span.model_request_end");
        end["model_usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 5 };
        session.Events.Add(end);
        JsonObject message = Event("agent.message");
        message["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = answer });
        session.Events.Add(message);
        JsonObject usage = Event("session.usage");
        usage["usage"] = Usage(session);
        session.Events.Add(usage);
        JsonObject idle = Event("session.status_idle");
        idle["stop_reason"] = new JsonObject { ["type"] = turn.IsError ? "budget_reached" : "end_turn" };
        session.Events.Add(idle);
        return (JsonObject)posted.DeepClone();
    }

    private JsonObject Event(string type) => new()
    {
        ["id"] = $"sevt_{++_events:D6}",
        ["type"] = type,
        ["processed_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    };

    private static JsonObject Usage(FakeSession session) => new()
    {
        ["list_cost"] = new JsonObject { ["amount"] = session.Cents.ToString(CultureInfo.InvariantCulture), ["currency"] = "USD" },
        ["active_seconds"] = 1,
    };

    private static string SessionJson(FakeSession session, string status) => new JsonObject
    {
        ["id"] = session.Id,
        ["type"] = "session",
        ["status"] = status,
        ["resources"] = new JsonArray(new JsonObject { ["id"] = "sesrsc_01", ["type"] = "github_repository" }),
        ["usage"] = status is "running" ? new JsonObject { ["list_cost"] = new JsonObject { ["amount"] = "0", ["currency"] = "USD" } } : Usage(session),
    }.ToJsonString();

    private (int Status, string Body) EventsOf(string id, string query)
    {
        IEnumerable<JsonObject> events = Find(id).Events;
        if (!query.Contains("order=asc", StringComparison.Ordinal))
        {
            events = events.Reverse();
        }

        var data = new JsonArray([.. events.Select(e => e.DeepClone())]);
        return (200, new JsonObject { ["data"] = data, ["next_page"] = null }.ToJsonString());
    }

    private (int Status, string Body) FilesOf(string query)
    {
        string scope = query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p[0] is "scope_id").Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault() ?? "";
        var data = _files.Where(f => scope.Length is 0 || f.Session == scope).Reverse().Select(f => new
        {
            id = f.Id,
            type = "file",
            filename = f.Name,
            size_bytes = Encoding.UTF8.GetByteCount(f.Content),
            created_at = f.Created.ToString("O", CultureInfo.InvariantCulture),
            downloadable = true,
        });
        return (200, JsonSerializer.Serialize(new { data, next_page = (string?)null, has_more = false }));
    }
}
