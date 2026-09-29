using System.Text.Json;

namespace Core;

/// <summary>A pull request: its number and web address.</summary>
public sealed record PullRequest(int Number, string HtmlUrl);

/// <summary>A check run as GitHub writes it; a conclusion that is null reads as the empty string.</summary>
public sealed record CheckRun(string Name, string Status, string Conclusion);

/// <summary>Opens pull requests and reads check runs, through a <see cref="GitHubClient"/>.</summary>
public sealed class PullRequests(GitHubClient client)
{
    /// <summary>Opens a pull request, or gives back the one already open from the branch.</summary>
    public async Task<PullRequest> OpenAsync(string owner, string repo, string head, string baseBranch, string title, string body, CancellationToken cancellationToken = default)
    {
        string basePath = $"/repos/{owner}/{repo}/pulls";
        string query = $"{basePath}?head={Uri.EscapeDataString(owner + ":" + head)}&state=open";
        JsonElement open = await client.SendAsync(HttpMethod.Get, query, null, cancellationToken).ConfigureAwait(false);
        if (open.ValueKind is JsonValueKind.Array && open.GetArrayLength() is not 0)
        {
            return ReadPullRequest(open[0]);
        }

        string json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["head"] = head,
            ["base"] = baseBranch,
            ["title"] = title,
            ["body"] = body,
        });
        JsonElement created = await client.SendAsync(HttpMethod.Post, basePath, json, cancellationToken).ConfigureAwait(false);
        return ReadPullRequest(created);
    }

    /// <summary>Reads the check runs on a commit.</summary>
    public async Task<IReadOnlyList<CheckRun>> ReadCheckRunsAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default)
    {
        JsonElement json = await client.SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/commits/{sha}/check-runs", null, cancellationToken).ConfigureAwait(false);
        var runs = new List<CheckRun>();
        foreach (JsonElement run in json.GetProperty("check_runs").EnumerateArray())
        {
            runs.Add(new CheckRun(Text(run, "name"), Text(run, "status"), Text(run, "conclusion")));
        }

        return runs;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String ? value.GetString() ?? "" : "";

    private static PullRequest ReadPullRequest(JsonElement json) =>
        new(json.GetProperty("number").GetInt32(), Text(json, "html_url"));
}
