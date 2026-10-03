using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned GitHub answers of the console program's scenarios are what the real API gives.</summary>
public sealed class ConsoleProgramIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    private static GitHubClient Client()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    [Fact]
    public async Task TheReadOfARepoGivesItsDefaultBranch()
    {
        JsonElement repo = await Client().SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}", null, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(repo.GetProperty("default_branch").GetString()));
    }

    [Fact]
    public async Task TheReadOfAMissingRepoGivesTheStatusCode404()
    {
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(
            () => Client().SendAsync(HttpMethod.Get, $"/repos/{Owner}/no-such-repo-{Guid.NewGuid():N}", null, TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }
}
