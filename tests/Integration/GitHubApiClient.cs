using System.Net.Http.Headers;
using System.Text.Json;

namespace Integration;

/// <summary>A thin client over GitHub's REST API for the integration suite.</summary>
public sealed class GitHubApiClient(string token)
{
    /// <summary>The commit a branch points at, with the author git recorded on it.</summary>
    public sealed record BranchInfo(string Sha, string AuthorName, string AuthorEmail);

    private readonly HttpClient _client = CreateClient(token);

    /// <summary>Returns <paramref name="branch"/>'s head commit and its author, as GitHub's branch API reports them.</summary>
    public async Task<BranchInfo> GetBranchAsync(string owner, string repo, string branch)
    {
        using HttpResponseMessage response = await _client
            .GetAsync($"repos/{owner}/{repo}/branches/{branch}")
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement commit = document.RootElement.GetProperty("commit");
        JsonElement author = commit.GetProperty("commit").GetProperty("author");
        return new BranchInfo(
            commit.GetProperty("sha").GetString() ?? "",
            author.GetProperty("name").GetString() ?? "",
            author.GetProperty("email").GetString() ?? "");
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("factory-integration-tests", "1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
