using System.Net.Http.Headers;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

public sealed class GitIntegrationTests
{
    private const string RepoSlug = "sbiondolillo/factory-scratch";
    private const string ApiBase = "https://api.github.com";
    private const string AuthorName = "Factory Integration";
    private const string AuthorEmail = "factory@example.com";

    [Fact]
    public async Task ClonesBranchesEditsAndPushes()
    {
        string run = Guid.NewGuid().ToString("N");
        string branch = $"factory/run-{run}";
        string file = $"factory-run-{run}.txt";
        (Git git, string workTree) = await CloneRepoAsync(RequireToken());

        string mainShaBefore = (await GetBranchAsync("main")).GetProperty("commit").GetProperty("sha").GetString()!;

        await git.CheckoutBranchAsync(branch);
        File.AppendAllText(Path.Combine(workTree, "README.md"), $"\nEdited by the factory integration run {run}.\n");
        File.WriteAllText(Path.Combine(workTree, file), $"created by the factory integration run {run}\n");
        await git.CommitAsync($"integration run {run}", AuthorName, AuthorEmail);
        await git.PushAsync(branch);
        string head = await git.ReadHeadShaAsync();

        JsonElement pushedBranch = await GetBranchAsync(branch);
        Assert.Equal(head, pushedBranch.GetProperty("commit").GetProperty("sha").GetString());

        JsonElement commit = (await GetAsync($"/repos/{RepoSlug}/commits/{head}")).GetProperty("commit");
        Assert.Equal(AuthorName, commit.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal(AuthorEmail, commit.GetProperty("author").GetProperty("email").GetString());

        string mainShaAfter = (await GetBranchAsync("main")).GetProperty("commit").GetProperty("sha").GetString()!;
        Assert.Equal(mainShaBefore, mainShaAfter);

        IReadOnlyList<string> changed = await git.ListChangedFilesSinceAsync("main");
        Assert.Equal(2, changed.Count);
        Assert.Contains("README.md", changed);
        Assert.Contains(file, changed);
    }

    [Fact]
    public async Task SecondPushToSameBranchIsRefused()
    {
        string run = Guid.NewGuid().ToString("N");
        string branch = $"factory/run-{run}";
        string token = RequireToken();

        (Git first, string firstTree) = await CloneRepoAsync(token);
        await first.CheckoutBranchAsync(branch);
        File.WriteAllText(Path.Combine(firstTree, $"first-{run}.txt"), "first\n");
        await first.CommitAsync($"integration run {run}, first", AuthorName, AuthorEmail);
        await first.PushAsync(branch);

        (Git second, string secondTree) = await CloneRepoAsync(token);
        await second.CheckoutBranchAsync(branch);
        File.WriteAllText(Path.Combine(secondTree, $"second-{run}.txt"), "second\n");
        await second.CommitAsync($"integration run {run}, second", AuthorName, AuthorEmail);

        File.WriteAllText(Path.Combine(firstTree, $"third-{run}.txt"), "third\n");
        await first.CommitAsync($"integration run {run}, third", AuthorName, AuthorEmail);
        await first.PushAsync(branch);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => second.PushAsync(branch));

        Assert.Contains("rejected", exception.Message);
    }

    [Fact]
    public async Task PushWithInvalidTokenFailsAuthentication()
    {
        string run = Guid.NewGuid().ToString("N");
        string branch = $"factory/run-{run}";
        (Git git, string workTree) = await CloneRepoAsync(RequireToken());

        await git.CheckoutBranchAsync(branch);
        File.WriteAllText(Path.Combine(workTree, $"push-{run}.txt"), "push\n");
        await git.CommitAsync($"integration run {run}", AuthorName, AuthorEmail);

        var invalid = new Git(new CommandRunner(), "invalid", workTree);
        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => invalid.PushAsync(branch));

        Assert.Contains("Authentication failed", exception.Message);
    }

    [Fact]
    public async Task UnknownBaseThrowsUnknownRevision()
    {
        (Git git, _) = await CloneRepoAsync(RequireToken());

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ListChangedFilesSinceAsync("no-such-base"));

        Assert.Contains("unknown revision", exception.Message);
    }

    private static async Task<(Git Git, string WorkTree)> CloneRepoAsync(string token)
    {
        string workTree = Path.Combine(Path.GetTempPath(), $"git-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workTree);

        var git = new Git(new CommandRunner(), token);
        await git.CloneAsync(RepoSlug, workTree);
        return (git, workTree);
    }

    private static Task<JsonElement> GetBranchAsync(string branch) => GetAsync($"/repos/{RepoSlug}/branches/{branch}");

    private static async Task<JsonElement> GetAsync(string path)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RequireToken());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using HttpResponseMessage response = await client.GetAsync($"{ApiBase}{path}");
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string RequireToken()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        if (token is null or { Length: 0 })
        {
            Assert.Fail("FACTORY_GITHUB_TOKEN is not set");
        }

        return token!;
    }
}
