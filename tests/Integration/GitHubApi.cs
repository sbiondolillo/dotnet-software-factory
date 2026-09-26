using System.Net.Http.Headers;
using System.Text.Json;

namespace Integration;

/// <summary>The author of a commit, as GitHub's branch API reports it.</summary>
public sealed record CommitAuthor(string Name, string Email);

/// <summary>The head of a branch, as GitHub's branch API reports it.</summary>
public sealed record BranchHead(string Sha, CommitAuthor Author);

/// <summary>A minimal client for the branch API of the shared scratch repo.</summary>
public sealed class GitHubApi(string token)
{
    private readonly HttpClient _client = CreateClient(token);

    /// <summary>Reads the head commit of the named branch of the scratch repo.</summary>
    public async Task<BranchHead> ReadBranchHeadAsync(string branch)
    {
        using HttpResponseMessage response = await _client.GetAsync($"repos/sbiondolillo/factory-scratch/branches/{branch}");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement commit = document.RootElement.GetProperty("commit");
        JsonElement author = commit.GetProperty("commit").GetProperty("author");
        return new BranchHead(
            commit.GetProperty("sha").GetString() ?? "",
            new CommitAuthor(author.GetProperty("name").GetString() ?? "", author.GetProperty("email").GetString() ?? ""));
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("factory-git-integration-tests", "1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
