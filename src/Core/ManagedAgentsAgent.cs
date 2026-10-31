using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core;

/// <summary>Runs each prompt as one message of a Managed Agents session over a GitHub repository resource, and reads the answer
/// from the session's events. The session clones <paramref name="sessionBranch"/>, and the builder role pushes to it after each message.</summary>
public sealed class ManagedAgentsAgent(
    ManagedAgentsClient client,
    GitHubAppTokens tokens,
    string owner,
    string repo,
    string sessionBranch,
    string baseCommit,
    string workTree,
    string rootDirectory,
    SessionRole role,
    int budgetCents,
    TextWriter? log = null,
    TimeProvider? time = null,
    Action<TimeSpan, ClaudeCodeResult>? onAnswer = null) : Agent, IAsyncDisposable
{
    private static readonly TimeSpan TokenAge = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan FileWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TurnWait = TimeSpan.FromHours(6);
    private const string Redacted = "<redacted>";

    // A line of events.jsonl keeps < and > as they are, so a reader of the store sees the text that the API sent.
    private static readonly JsonSerializerOptions EventLine = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly List<string> _uploads = [];
    private string? _sessionId;
    private string? _resourceId;
    private string? _model;
    private string? _token;
    private long _tokenStamp;
    private string? _lastMessageId;
    private string? _lastWrittenId;
    private decimal _cents;
    private bool _disposed;

    private ManagedAgentsClient Client { get; } = client;

    private string Owner { get; } = owner;

    private string Repo { get; } = repo;

    private string SessionBranch { get; } = sessionBranch;

    private string BaseCommit { get; } = baseCommit;

    private string WorkTree { get; } = workTree;

    private string RootDirectory { get; } = rootDirectory;

    private SessionRole Role { get; } = role;

    private int BudgetCents { get; } = budgetCents;

    private TextWriter? Log { get; } = log;

    private TimeProvider Time { get; } = time ?? TimeProvider.System;

    private Action<TimeSpan, ClaudeCodeResult>? OnAnswer { get; } = onAnswer;

    /// <summary>Mints an App token, with write access when the argument is true. A test replaces it.</summary>
    internal Func<bool, CancellationToken, Task<string>> MintToken { get; init; } =
        (write, cancellationToken) => tokens.MintAsync(repo, write, cancellationToken);

    /// <summary>Revokes an App token. A test replaces it.</summary>
    internal Func<string, CancellationToken, Task> RevokeToken { get; init; } = tokens.RevokeAsync;

    /// <summary>The sessions this agent opened, with the cost each one reached.</summary>
    public IReadOnlyList<SessionCost> Sessions =>
        _sessionId is null ? [] : [new SessionCost(_sessionId, RoleName, (int)Math.Round(_cents, MidpointRounding.AwayFromZero))];

    private string RoleName => Role is SessionRole.Builder ? "builder" : "reviewer";

    private string SessionPath => $"/v1/sessions/{_sessionId}";

    /// <inheritdoc />
    public override async Task<ClaudeCodeResult> RunAsync(
        string prompt,
        string model,
        int maxTurns,
        string phase,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_model is not null && _model != model)
        {
            throw new ClaudeCodeException(
                $"The session {_sessionId} runs the model {_model}, and the {phase} prompt asks for the model {model}. A session keeps the model of its first prompt.");
        }

        await ProgressLine.WriteAsync(Log, $"agent: {phase} start").ConfigureAwait(false);
        long started = Time.GetTimestamp();
        decimal before = _cents;
        if (_sessionId is null)
        {
            await CreateSessionAsync(prompt, model, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RotateWhenOldAsync(cancellationToken).ConfigureAwait(false);
            var body = new JsonObject { ["events"] = new JsonArray(UserMessage(Compose(prompt))) };
            await Client.SendAsync(HttpMethod.Post, $"{SessionPath}/events", body, cancellationToken).ConfigureAwait(false);
        }

        await ProgressLine.WriteAsync(
            Log,
            string.Create(CultureInfo.InvariantCulture, $"session: {phase} max turns {maxTurns}, not enforced: the budget bounds the session")).ConfigureAwait(false);

        Turn turn = await WaitForTurnAsync(cancellationToken).ConfigureAwait(false);
        await WriteEventsAsync(turn.NewEvents, cancellationToken).ConfigureAwait(false);
        bool pushedAgain = false;
        if (Role is SessionRole.Builder && PushFailed(turn))
        {
            await ProgressLine.WriteAsync(Log, $"session: {phase} push failed with {SessionPrompt.AuthenticationFailed}, token rotated").ConfigureAwait(false);
            await RotateAsync(cancellationToken).ConfigureAwait(false);
            var again = new JsonObject { ["events"] = new JsonArray(UserMessage(SessionPrompt.PushAgain(Repo, SessionBranch))) };
            await Client.SendAsync(HttpMethod.Post, $"{SessionPath}/events", again, cancellationToken).ConfigureAwait(false);
            Turn retry = await WaitForTurnAsync(cancellationToken).ConfigureAwait(false);
            await WriteEventsAsync(retry.NewEvents, cancellationToken).ConfigureAwait(false);
            turn = new Turn([.. turn.NewEvents, .. retry.NewEvents], [.. turn.Events, .. retry.Events]);
            pushedAgain = true;
        }

        JsonElement session = await Client.SendAsync(HttpMethod.Get, SessionPath, null, cancellationToken).ConfigureAwait(false);
        _cents = Math.Max(_cents, ListCost(session));

        ClaudeCodeResult answer = await AnswerAsync(turn, Normalize((_cents - before) / 100m), cancellationToken).ConfigureAwait(false);
        if (pushedAgain && PushFailed(turn))
        {
            answer = answer with { IsError = true };
        }

        TimeSpan elapsed = Time.GetElapsedTime(started);
        long seconds = (long)elapsed.TotalSeconds;
        OnAnswer?.Invoke(elapsed, answer);
        await ProgressLine.WriteAsync(
            Log,
            string.Create(CultureInfo.InvariantCulture, $"agent: {phase} end, {answer.NumTurns} turns, ${answer.CostUsd}, {seconds} s")).ConfigureAwait(false);
        return answer;
    }

    /// <summary>Interrupts a running session and revokes the App token.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_sessionId is not null)
        {
            try
            {
                JsonElement session = await Client.SendAsync(HttpMethod.Get, SessionPath, null, CancellationToken.None).ConfigureAwait(false);
                if (Text(session, "status") is "running" or "rescheduling")
                {
                    var body = new JsonObject { ["events"] = new JsonArray(new JsonObject { ["type"] = "user.interrupt" }) };
                    await Client.SendAsync(HttpMethod.Post, $"{SessionPath}/events", body, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (ClaudeCodeException)
            {
                // The interrupt is best effort: a session that refuses it ends on its own at the budget.
            }
            catch (HttpRequestException)
            {
                // The same, for a transport fault on the way to the API.
            }
            catch (TaskCanceledException)
            {
                // The same, for a timeout of the request.
            }
        }

        if (_token is not null)
        {
            await RevokeQuietlyAsync(_token).ConfigureAwait(false);
            _token = null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Type(JsonElement e) => Text(e, "type") ?? "";

    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    private static decimal ListCost(JsonElement session)
    {
        if (session.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind is JsonValueKind.Object
            && usage.TryGetProperty("list_cost", out JsonElement cost) && cost.ValueKind is JsonValueKind.Object
            && cost.TryGetProperty("amount", out JsonElement amount))
        {
            if (amount.ValueKind is JsonValueKind.Number)
            {
                return amount.GetDecimal();
            }

            return amount.ValueKind is JsonValueKind.String
                && decimal.TryParse(amount.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cents)
                ? cents
                : 0m;
        }

        return 0m;
    }

    private static string MessageText(JsonElement e) =>
        e.TryGetProperty("content", out JsonElement content) && content.ValueKind is JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Where(c => Type(c) is "text").Select(c => Text(c, "text") ?? ""))
            : "";

    private static string LastText(Turn turn) => turn.Events.Where(e => Type(e) is "agent.message").Select(MessageText).LastOrDefault() ?? "";

    /// <summary>True when the turn ended by itself and its final answer holds the text of a push that GitHub refused for the token.</summary>
    private static bool PushFailed(Turn turn) =>
        turn.Events.Where(e => Type(e) is "session.status_idle").Select(StopReason).LastOrDefault() is null or "end_turn"
        && LastText(turn).Contains(SessionPrompt.AuthenticationFailed, StringComparison.Ordinal);

    private static string? StopReason(JsonElement idle)
    {
        foreach (string name in (string[])["stop_reason", "stop_details"])
        {
            if (idle.TryGetProperty(name, out JsonElement stop) && Text(stop, "type") is { } type)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Splits the last line <c>outputs: a b</c> off the answer, and gives the names it lists, or null with no such line.</summary>
    private static (string Text, IReadOnlyList<string>? Outputs) SplitOutputs(string text)
    {
        string trimmed = text.TrimEnd();
        int lineStart = trimmed.LastIndexOf('\n') + 1;
        string last = trimmed[lineStart..].Trim().Trim('`');
        if (!last.StartsWith(SessionPrompt.OutputsLine, StringComparison.Ordinal))
        {
            return (text, null);
        }

        string[] names = last[SessionPrompt.OutputsLine.Length..].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        return (trimmed[..lineStart].TrimEnd(), [.. names.Select(Path.GetFileName).OfType<string>().Where(n => n.Length > 0)]);
    }

    private static JsonObject UserMessage(string text) =>
        new()
        {
            ["type"] = "user.message",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };

    private static bool IsSafeName(string name) =>
        name.Length > 0 && name is not "." and not ".." && Path.GetFileName(name) == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private string Compose(string prompt) => SessionPrompt.Compose(prompt, Repo, SessionBranch, BaseCommit, _uploads, Role);

    private async Task CreateSessionAsync(string prompt, string model, CancellationToken cancellationToken)
    {
        if (Client.AgentId.Length is 0 || Client.EnvironmentId.Length is 0)
        {
            throw new ClaudeCodeException("ManagedAgentsClient has no AgentId or no EnvironmentId, so no session can start.");
        }

        var resources = new JsonArray();
        string factory = Path.Combine(WorkTree, ".factory");
        if (Directory.Exists(factory))
        {
            foreach (string path in Directory.EnumerateFiles(factory).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                if (!IsSafeName(name))
                {
                    continue;
                }

                string fileId = await Client.UploadAsync(path, cancellationToken).ConfigureAwait(false);
                _uploads.Add(name);
                resources.Add(new JsonObject { ["type"] = "file", ["file_id"] = fileId, ["mount_path"] = $"/{name}" });
            }
        }

        _token = await MintToken(Role is SessionRole.Builder, cancellationToken).ConfigureAwait(false);
        _tokenStamp = Time.GetTimestamp();
        resources.Insert(0, new JsonObject
        {
            ["type"] = "github_repository",
            ["url"] = $"https://github.com/{Owner}/{Repo}",
            ["mount_path"] = $"/workspace/{Repo}",
            ["authorization_token"] = _token,
        });
        var body = new JsonObject
        {
            ["agent"] = new JsonObject { ["type"] = "agent_with_overrides", ["id"] = Client.AgentId, ["model"] = new JsonObject { ["id"] = model } },
            ["environment_id"] = Client.EnvironmentId,
            ["title"] = $"dsf {Owner}/{Repo} {RoleName} {SessionBranch}",
            ["metadata"] = new JsonObject { ["repo"] = $"{Owner}/{Repo}", ["branch"] = SessionBranch, ["role"] = RoleName },
            ["budget"] = new JsonObject
            {
                ["type"] = "limit",
                ["max_list_cost"] = new JsonObject { ["amount"] = BudgetCents.ToString(CultureInfo.InvariantCulture), ["currency"] = "USD" },
            },
            ["resources"] = resources,
            ["initial_events"] = new JsonArray(UserMessage(Compose(prompt))),
        };
        JsonElement session = await Client.SendAsync(HttpMethod.Post, "/v1/sessions", body, cancellationToken).ConfigureAwait(false);
        _sessionId = Text(session, "id") ?? throw new ClaudeCodeException("POST /v1/sessions answered with no session id.");
        _model = model;
        _resourceId = RepositoryResourceId(session);
        await ProgressLine.WriteAsync(Log, $"session: start {_sessionId}").ConfigureAwait(false);
    }

    private static string? RepositoryResourceId(JsonElement session) =>
        session.TryGetProperty("resources", out JsonElement resources) && resources.ValueKind is JsonValueKind.Array
            ? resources.EnumerateArray().Where(r => Type(r) is "github_repository").Select(r => Text(r, "id")).FirstOrDefault(id => id is not null)
            : null;

    private async Task RotateWhenOldAsync(CancellationToken cancellationToken)
    {
        if (Time.GetElapsedTime(_tokenStamp) >= TokenAge)
        {
            await RotateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Mints a new token, gives it to the repository resource of the session, and revokes the old one.</summary>
    private async Task RotateAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is null || _token is null)
        {
            return;
        }

        if (_resourceId is null)
        {
            JsonElement session = await Client.SendAsync(HttpMethod.Get, SessionPath, null, cancellationToken).ConfigureAwait(false);
            _resourceId = RepositoryResourceId(session)
                ?? throw new ClaudeCodeException($"The session {_sessionId} lists no github_repository resource, so its token cannot be rotated.");
        }

        string fresh = await MintToken(Role is SessionRole.Builder, cancellationToken).ConfigureAwait(false);
        try
        {
            await Client.SendAsync(
                HttpMethod.Post,
                $"{SessionPath}/resources/{_resourceId}",
                new JsonObject { ["authorization_token"] = fresh },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClaudeCodeException)
        {
            await RevokeQuietlyAsync(fresh).ConfigureAwait(false);
            throw;
        }

        string old = _token;
        _token = fresh;
        _tokenStamp = Time.GetTimestamp();
        await RevokeQuietlyAsync(old).ConfigureAwait(false);
    }

    private async Task RevokeQuietlyAsync(string token)
    {
        try
        {
            await RevokeToken(token, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // The revoke is best effort: an installation token expires on its own within an hour.
        }
        catch (GitHubRequestException)
        {
            // The same, for a status GitHub answers, such as a token that is revoked already.
        }
        catch (TaskCanceledException)
        {
            // The same, for a timeout of the request.
        }
    }

    /// <summary>Reads the event list, newest first, from the newest event back to the first one <paramref name="stop"/>
    /// matches, and gives the events read with that one last. Found is false when the list ends first.</summary>
    private async Task<(List<JsonElement> Events, bool Found)> ReadBackAsync(Func<JsonElement, bool> stop, CancellationToken cancellationToken)
    {
        var events = new List<JsonElement>();
        string? page = null;
        do
        {
            string path = $"{SessionPath}/events?order=desc&limit=100" + (page is null ? "" : $"&page={Uri.EscapeDataString(page)}");
            JsonElement answer = await Client.SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
            if (answer.TryGetProperty("data", out JsonElement data) && data.ValueKind is JsonValueKind.Array)
            {
                foreach (JsonElement e in data.EnumerateArray())
                {
                    events.Add(e);
                    if (stop(e))
                    {
                        return (events, true);
                    }
                }
            }

            page = Text(answer, "next_page");
        }
        while (page is { Length: > 0 });
        return (events, false);
    }

    /// <summary>Polls the event list until a <c>session.status_idle</c> follows the newest <c>user.message</c>, the message
    /// this call posted. An idle before that message belongs to the turn before it.</summary>
    private async Task<Turn> WaitForTurnAsync(CancellationToken cancellationToken)
    {
        string messageId;
        long waitStarted = Time.GetTimestamp();
        while (true)
        {
            (List<JsonElement> newest, bool found) = await ReadBackAsync(e => Type(e) is "user.message", cancellationToken).ConfigureAwait(false);
            if (found && Text(newest[^1], "id") is { } id && id != _lastMessageId
                && newest.Take(newest.Count - 1).Any(e => Type(e) is "session.status_idle"))
            {
                messageId = id;
                break;
            }

            JsonElement session = await Client.SendAsync(HttpMethod.Get, SessionPath, null, cancellationToken).ConfigureAwait(false);
            if (Text(session, "status") is "terminated")
            {
                throw new ClaudeCodeException($"The session {_sessionId} ended with the status terminated before its turn went idle.");
            }

            if (Time.GetElapsedTime(waitStarted) >= TurnWait)
            {
                throw new ClaudeCodeException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The turn of the session {_sessionId} did not go idle within {TurnWait.TotalHours} hours."));
            }

            await Client.Delay(ManagedAgentsClient.PollDelay, cancellationToken).ConfigureAwait(false);
            await RotateWhenOldAsync(cancellationToken).ConfigureAwait(false);
        }

        string? lastWritten = _lastWrittenId;
        (List<JsonElement> fresh, bool reached) = await ReadBackAsync(e => Text(e, "id") == lastWritten, cancellationToken).ConfigureAwait(false);
        if (reached)
        {
            fresh.RemoveAt(fresh.Count - 1);
        }

        fresh.Reverse();
        int start = fresh.FindIndex(e => Text(e, "id") == messageId);
        List<JsonElement> turn = start < 0 ? fresh : fresh[start..];
        _lastMessageId = messageId;
        if (fresh.Count > 0)
        {
            _lastWrittenId = Text(fresh[^1], "id") ?? _lastWrittenId;
        }

        return new Turn(fresh, turn);
    }

    private async Task WriteEventsAsync(List<JsonElement> events, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(RootDirectory, ".factory", "sandbox-home", _sessionId!);
        Directory.CreateDirectory(directory);
        var lines = new StringBuilder();
        foreach (JsonElement e in events)
        {
            JsonNode? node = JsonSerializer.SerializeToNode(e);
            Redact(node);
            lines.Append(node is null ? "null" : node.ToJsonString(EventLine)).Append('\n');
        }

        await File.AppendAllTextAsync(Path.Combine(directory, "events.jsonl"), lines.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets each property named <c>authorization_token</c> in <paramref name="node"/>, at any depth, to the string
    /// <c>&lt;redacted&gt;</c>. The events go to a store, and a token in them would reach it.</summary>
    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject value:
                foreach (string key in value.Select(property => property.Key).ToList())
                {
                    if (key is "authorization_token")
                    {
                        value[key] = Redacted;
                    }
                    else
                    {
                        Redact(value[key]);
                    }
                }

                break;
            case JsonArray items:
                foreach (JsonNode? item in items)
                {
                    Redact(item);
                }

                break;
            default:
                break;
        }
    }

    private async Task<ClaudeCodeResult> AnswerAsync(Turn turn, decimal cost, CancellationToken cancellationToken)
    {
        string text = LastText(turn);
        int turns = turn.Events.Count(e => Type(e) is "span.model_request_end");
        string? stop = turn.Events.Where(e => Type(e) is "session.status_idle").Select(StopReason).LastOrDefault();
        (string result, IReadOnlyList<string>? outputs) = SplitOutputs(text);

        if (stop is "budget_reached")
        {
            await DownloadAsync(turn, [], cancellationToken).ConfigureAwait(false);
            string budget = string.Create(
                CultureInfo.InvariantCulture,
                $"The session {_sessionId} reached its budget of {BudgetCents} cents (${Normalize(BudgetCents / 100m)}) before the agent finished.");
            return new ClaudeCodeResult(result.Length > 0 ? $"{budget} {result}" : budget, turns, cost, true);
        }

        if (stop is not null and not "end_turn")
        {
            await DownloadAsync(turn, [], cancellationToken).ConfigureAwait(false);
            return new ClaudeCodeResult($"The session {_sessionId} stopped with the reason {stop}. {result}".TrimEnd(), turns, cost, true);
        }

        string? missing = await DownloadAsync(turn, outputs ?? [], cancellationToken).ConfigureAwait(false);
        if (missing is not null)
        {
            return new ClaudeCodeResult(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The answer names the output file {missing}, and the file list of the session {_sessionId} did not hold it after {FileWait.TotalSeconds} s."),
                turns,
                cost,
                true);
        }

        return new ClaudeCodeResult(result, turns, cost, result.StartsWith(SessionPrompt.PreambleFailed, StringComparison.Ordinal));
    }

    /// <summary>Downloads into <c>.factory/</c> of the work tree each file the session created after the message, the newest
    /// of each name. It waits for each name in <paramref name="expected"/>, and gives the first one missing at the end of the wait.</summary>
    private async Task<string?> DownloadAsync(Turn turn, IReadOnlyList<string> expected, CancellationToken cancellationToken)
    {
        DateTimeOffset posted = turn.Events.Count > 0 && Text(turn.Events[0], "processed_at") is { } stamp
            && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
            ? at
            : DateTimeOffset.MinValue;
        int polls = (int)(FileWait / ManagedAgentsClient.PollDelay);
        Dictionary<string, (string Id, DateTimeOffset At)> files = [];
        string? missing = null;
        for (int poll = 0; ; poll++)
        {
            files = await ListFilesAsync(posted, cancellationToken).ConfigureAwait(false);
            missing = expected.FirstOrDefault(name => !files.ContainsKey(name));
            if (missing is null || poll >= polls)
            {
                break;
            }

            await Client.Delay(ManagedAgentsClient.PollDelay, cancellationToken).ConfigureAwait(false);
        }

        string factory = Path.Combine(WorkTree, ".factory");
        foreach ((string name, (string id, _)) in files)
        {
            byte[] bytes = await Client.DownloadAsync(id, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(factory);
            await File.WriteAllBytesAsync(Path.Combine(factory, name), bytes, cancellationToken).ConfigureAwait(false);
        }

        return missing;
    }

    private async Task<Dictionary<string, (string Id, DateTimeOffset At)>> ListFilesAsync(DateTimeOffset after, CancellationToken cancellationToken)
    {
        Dictionary<string, (string Id, DateTimeOffset At)> newest = new(StringComparer.Ordinal);
        string query = $"/v1/files?scope_id={Uri.EscapeDataString(_sessionId!)}";
        string? cursor = null;
        do
        {
            JsonElement answer = await Client.SendAsync(HttpMethod.Get, query + (cursor ?? ""), null, cancellationToken).ConfigureAwait(false);
            if (answer.TryGetProperty("data", out JsonElement data) && data.ValueKind is JsonValueKind.Array)
            {
                foreach (JsonElement file in data.EnumerateArray())
                {
                    if (Text(file, "id") is not { } id || Text(file, "filename") is not { } name || !IsSafeName(name)
                        || Text(file, "created_at") is not { } created
                        || !DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
                        || at <= after)
                    {
                        continue;
                    }

                    if (!newest.TryGetValue(name, out (string Id, DateTimeOffset At) known) || at > known.At)
                    {
                        newest[name] = (id, at);
                    }
                }
            }

            cursor = Text(answer, "next_page") is { Length: > 0 } next
                ? $"&page={Uri.EscapeDataString(next)}"
                : answer.TryGetProperty("has_more", out JsonElement more) && more.ValueKind is JsonValueKind.True && Text(answer, "last_id") is { } last
                    ? $"&after_id={Uri.EscapeDataString(last)}"
                    : null;
        }
        while (cursor is not null);
        return newest;
    }

    private sealed record Turn(List<JsonElement> NewEvents, List<JsonElement> Events);
}
