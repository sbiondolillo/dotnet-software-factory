using System.Net.Http.Headers;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

public sealed class GitTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    [Fact]
    public async Task ACloneCheckoutCommitAndPushCycleIsReportedByGithub()
    {
        string token = RequireToken();
        using HttpClient client = Client(token);
        string cloneDirectory = Path.Combine(Path.GetTempPath(), $"factory-integration-{Guid.NewGuid():N}");

        try
        {
            string branch = $"factory-integration-{Guid.NewGuid():N}";
            string fileName = $"factory-integration-{Guid.NewGuid():N}.md";
            string authorName = "Factory Integration";
            string authorEmail = "factory@example.com";
            Git git = new(new CommandRunner());

            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string mainShaBefore = (await ReadBranch(client, "main", cancellationToken)).CommitSha;

            await git.CloneAsync(Owner, Repo, token, cloneDirectory);
            await git.CheckoutNewBranchAsync(cloneDirectory, branch);

            string readme = Path.Combine(cloneDirectory, "README.md");
            string readmeText = File.Exists(readme) ? await File.ReadAllTextAsync(readme, cancellationToken) : string.Empty;
            await File.WriteAllTextAsync(readme, $"{readmeText}Integration run {branch}.\n", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(cloneDirectory, fileName), $"Created by the integration run {branch}.\n", cancellationToken);

            await git.CommitAllAsync(cloneDirectory, $"Integration run {branch}", authorName, authorEmail);
            await git.PushAsync(cloneDirectory, branch, token);

            string headSha = await git.ReadHeadShaAsync(cloneDirectory);
            BranchInfo branchInfo = await ReadBranch(client, branch, cancellationToken);
            Assert.Equal(headSha, branchInfo.CommitSha);
            Assert.Equal(authorName, branchInfo.AuthorName);
            Assert.Equal(authorEmail, branchInfo.AuthorEmail);
            Assert.Equal(authorName, branchInfo.CommitterName);
            Assert.Equal(authorEmail, branchInfo.CommitterEmail);

            IReadOnlyList<string> changed = await git.ListChangedFilesAsync(cloneDirectory, "main");
            Assert.Equal(new[] { fileName, "README.md" }, changed.OrderBy(name => name, StringComparer.Ordinal).ToArray());

            Assert.Equal(mainShaBefore, (await ReadBranch(client, "main", cancellationToken)).CommitSha);
        }
        finally
        {
            if (Directory.Exists(cloneDirectory))
            {
                Directory.Delete(cloneDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task APushWithAnInvalidTokenGetsTheFailedAuthentication()
    {
        string token = RequireToken();
        string cloneDirectory = Path.Combine(Path.GetTempPath(), $"factory-integration-{Guid.NewGuid():N}");

        try
        {
            string branch = $"factory-integration-{Guid.NewGuid():N}";
            Git git = new(new CommandRunner());
            await git.CloneAsync(Owner, Repo, token, cloneDirectory);
            await git.CheckoutNewBranchAsync(cloneDirectory, branch);

            GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
                () => git.PushAsync(cloneDirectory, branch, "invalid"));

            Assert.Equal("push", exception.Subcommand);
            Assert.Contains("Authentication", exception.StandardError);
        }
        finally
        {
            if (Directory.Exists(cloneDirectory))
            {
                Directory.Delete(cloneDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ABaseThatGitCannotResolveGetsTheUnknownRevision()
    {
        string token = RequireToken();
        string cloneDirectory = Path.Combine(Path.GetTempPath(), $"factory-integration-{Guid.NewGuid():N}");

        try
        {
            Git git = new(new CommandRunner());
            await git.CloneAsync(Owner, Repo, token, cloneDirectory);

            GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
                () => git.ListChangedFilesAsync(cloneDirectory, "no-such-base"));

            Assert.Equal("diff", exception.Subcommand);
            Assert.Contains("no-such-base", exception.StandardError);
        }
        finally
        {
            if (Directory.Exists(cloneDirectory))
            {
                Directory.Delete(cloneDirectory, recursive: true);
            }
        }
    }

    private static string RequireToken()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Assert.Fail("FACTORY_GITHUB_TOKEN is not set");
        }

        return token!;
    }

    private static HttpClient Client(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("factory", "integration"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<BranchInfo> ReadBranch(HttpClient client, string branch, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync($"/repos/{Owner}/{Repo}/branches/{branch}", cancellationToken);
        response.EnsureSuccessStatusCode();
        JsonElement commit = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("commit");
        JsonElement author = commit.GetProperty("commit").GetProperty("author");
        JsonElement committer = commit.GetProperty("commit").GetProperty("committer");
        return new BranchInfo(
            Get(commit, "sha"),
            Get(author, "name"),
            Get(author, "email"),
            Get(committer, "name"),
            Get(committer, "email"));
    }

    private static string Get(JsonElement element, string property)
        => element.GetProperty(property).GetString() ?? string.Empty;

    private sealed record BranchInfo(string CommitSha, string AuthorName, string AuthorEmail, string CommitterName, string CommitterEmail);
}
