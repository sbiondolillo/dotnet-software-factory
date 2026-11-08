using System.Globalization;
using System.Text.Json;

namespace Core;

/// <summary>Opens pull requests and reads check runs through a <see cref="GitHubClient"/>. The wait for a pushed head waits with
/// <paramref name="delay"/> on the clock <paramref name="time"/>.</summary>
public sealed class PullRequests(
    GitHubClient client,
    GitHubClient? checksClient = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeProvider? time = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((span, token) => Task.Delay(span, token));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Opens a pull request, or gives back the one already open from the branch.</summary>
    public async Task<PullRequest> OpenAsync(RepoName repo, string head, string baseBranch, string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        string query = $"head={Uri.EscapeDataString(repo.Owner + ":" + head)}&state=open";
        JsonElement open = await client.SendAsync(HttpMethod.Get, $"/repos/{repo.Owner}/{repo.Name}/pulls?{query}", null, cancellationToken).ConfigureAwait(false);
        if (open.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement existing in open.EnumerateArray())
            {
                return Read(existing);
            }
        }

        string json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["head"] = head,
            ["base"] = baseBranch,
            ["title"] = title,
            ["body"] = body,
        });
        JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{repo.Owner}/{repo.Name}/pulls", json, cancellationToken).ConfigureAwait(false);
        return Read(created);
    }

    /// <summary>Reads every check run on a commit, with the checks client when one was given.</summary>
    public async Task<IReadOnlyList<CheckRun>> ReadCheckRunsAsync(RepoName repo, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        JsonElement json = await (checksClient ?? client).SendAsync(HttpMethod.Get, $"/repos/{repo.Owner}/{repo.Name}/commits/{sha}/check-runs", null, cancellationToken).ConfigureAwait(false);
        var runs = new List<CheckRun>();
        if (json.TryGetProperty("check_runs", out JsonElement array) && array.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement run in array.EnumerateArray())
            {
                string conclusion = run.TryGetProperty("conclusion", out JsonElement c) && c.ValueKind is JsonValueKind.String ? c.GetString() ?? "" : "";
                runs.Add(new CheckRun(run.GetProperty("name").GetString() ?? "", run.GetProperty("status").GetString() ?? "", conclusion));
            }
        }

        return runs;
    }

    /// <summary>Reads the sha of the head commit of a pull request.</summary>
    public async Task<string> ReadHeadShaAsync(IssueNumber pullRequest, CancellationToken cancellationToken = default)
    {
        JsonElement json = await ReadAsync(pullRequest, cancellationToken).ConfigureAwait(false);
        return json.GetProperty("head").GetProperty("sha").GetString() ?? "";
    }

    /// <summary>
    /// Reads the head until it is <paramref name="pushed"/> or <paramref name="grace"/> passes, polling each <paramref name="wait"/>.
    /// Gives the head it last read. GitHub reads the commit before a push for about 4 seconds after it.
    /// </summary>
    public async Task<string> ReadHeadShaAsync(IssueNumber pullRequest, string pushed, TimeSpan grace, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        long start = _time.GetTimestamp();
        while (true)
        {
            string sha = await ReadHeadShaAsync(pullRequest, cancellationToken).ConfigureAwait(false);
            if (sha == pushed || _time.GetElapsedTime(start) >= grace)
            {
                return sha;
            }

            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the node id of a pull request, which GraphQL mutations take.</summary>
    public async Task<string> ReadNodeIdAsync(IssueNumber pullRequest, CancellationToken cancellationToken = default)
    {
        JsonElement json = await ReadAsync(pullRequest, cancellationToken).ConfigureAwait(false);
        return json.TryGetProperty("node_id", out JsonElement id) ? id.GetString() ?? "" : "";
    }

    /// <summary>Reads the name of the head branch of a pull request.</summary>
    public async Task<string> ReadHeadBranchAsync(IssueNumber pullRequest, CancellationToken cancellationToken = default)
    {
        JsonElement json = await ReadAsync(pullRequest, cancellationToken).ConfigureAwait(false);
        return json.GetProperty("head").GetProperty("ref").GetString() ?? "";
    }

    /// <summary>Reads the body of a pull request; empty when it has none.</summary>
    public async Task<string> ReadBodyAsync(IssueNumber pullRequest, CancellationToken cancellationToken = default)
    {
        JsonElement json = await ReadAsync(pullRequest, cancellationToken).ConfigureAwait(false);
        return json.ValueKind is JsonValueKind.Object && json.TryGetProperty("body", out JsonElement body) && body.ValueKind is JsonValueKind.String ? body.GetString() ?? "" : "";
    }

    /// <summary>The number of the issue that a line <c>Closes #&lt;n&gt;</c> of a pull request body names, or null when there is none.</summary>
    public static int? ParseClosingIssue(string? body)
    {
        if (body is null)
        {
            return null;
        }

        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            body,
            @"^\s*Closes #(\d+)\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : null;
    }

    private Task<JsonElement> ReadAsync(IssueNumber pullRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return client.SendAsync(HttpMethod.Get, $"/repos/{pullRequest.Repo.Owner}/{pullRequest.Repo.Name}/pulls/{pullRequest.Number}", null, cancellationToken);
    }

    private static PullRequest Read(JsonElement json) =>
        new(json.GetProperty("number").GetInt32(), json.GetProperty("html_url").GetString() ?? "");
}
