using System.Text.Json;

namespace Core;

/// <summary>Opens pull requests and reads check runs through a <see cref="GitHubClient"/>.</summary>
public sealed class PullRequests(GitHubClient client)
{
    /// <summary>Opens a pull request, or gives back the one already open from the branch.</summary>
    public async Task<PullRequest> OpenAsync(string owner, string repo, string head, string baseBranch, string title, string body, CancellationToken cancellationToken = default)
    {
        string query = $"head={Uri.EscapeDataString(owner + ":" + head)}&state=open";
        JsonElement open = await client.SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/pulls?{query}", null, cancellationToken).ConfigureAwait(false);
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
        JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{owner}/{repo}/pulls", json, cancellationToken).ConfigureAwait(false);
        return Read(created);
    }

    /// <summary>Reads every check run on a commit.</summary>
    public async Task<IReadOnlyList<CheckRun>> ReadCheckRunsAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default)
    {
        JsonElement json = await client.SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/commits/{sha}/check-runs", null, cancellationToken).ConfigureAwait(false);
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
    public async Task<string> ReadHeadShaAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement json = await client.SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/pulls/{number}", null, cancellationToken).ConfigureAwait(false);
        return json.GetProperty("head").GetProperty("sha").GetString() ?? "";
    }

    /// <summary>Reads the name of the head branch of a pull request.</summary>
    public async Task<string> ReadHeadBranchAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement json = await client.SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/pulls/{number}", null, cancellationToken).ConfigureAwait(false);
        return json.GetProperty("head").GetProperty("ref").GetString() ?? "";
    }

    private static PullRequest Read(JsonElement json) =>
        new(json.GetProperty("number").GetInt32(), json.GetProperty("html_url").GetString() ?? "");
}
