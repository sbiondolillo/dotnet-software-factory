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
    private const string ClosingIssuesQuery = "query($owner: String!, $name: String!, $number: Int!) { repository(owner: $owner, name: $name) { pullRequest(number: $number) { closingIssuesReferences(first: 100) { nodes { number repository { name owner { login } } } } } } }";

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

    /// <summary>
    /// Reads the issue that the merge of a pull request closes, as GitHub lists it under <c>closingIssuesReferences</c>: the
    /// first issue of the pull request's own repo, or null when GitHub lists none. An issue of another repo is skipped, and the
    /// names of the repo compare without case.
    /// </summary>
    public async Task<IssueNumber?> ReadClosingIssueAsync(IssueNumber pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        RepoName repo = pullRequest.Repo;
        var variables = new { owner = repo.Owner, name = repo.Name, number = pullRequest.Number };
        JsonElement data = await client.SendGraphQLAsync(ClosingIssuesQuery, variables, cancellationToken).ConfigureAwait(false);
        JsonElement nodes = data.GetProperty("repository").GetProperty("pullRequest").GetProperty("closingIssuesReferences").GetProperty("nodes");
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            JsonElement repository = node.GetProperty("repository");
            string owner = repository.GetProperty("owner").GetProperty("login").GetString() ?? "";
            string name = repository.GetProperty("name").GetString() ?? "";
            if (string.Equals(owner, repo.Owner, StringComparison.OrdinalIgnoreCase) && string.Equals(name, repo.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new IssueNumber(repo, node.GetProperty("number").GetInt32());
            }
        }

        return null;
    }

    private Task<JsonElement> ReadAsync(IssueNumber pullRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return client.SendAsync(HttpMethod.Get, $"/repos/{pullRequest.Repo.Owner}/{pullRequest.Repo.Name}/pulls/{pullRequest.Number}", null, cancellationToken);
    }

    private static PullRequest Read(JsonElement json) =>
        new(json.GetProperty("number").GetInt32(), json.GetProperty("html_url").GetString() ?? "");
}
