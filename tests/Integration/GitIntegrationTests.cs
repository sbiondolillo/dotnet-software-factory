using System.Net.Http.Headers;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

public sealed class GitIntegrationTests
{
    private const string Repo = "sbiondolillo/factory-scratch";
    private const string CloneUrl = "https://github.com/sbiondolillo/factory-scratch.git";

    [Fact]
    public async Task ACloneBranchCommitAndPushDeliversABranchGitHubReports()
    {
        string token = RequireToken();
        string branch = UniqueBranch();
        string cloneDirectory = FreshCloneDirectory();
        try
        {
            using HttpClient api = ApiClient(token);
            string mainBefore = (await GetBranch(api, "main")).CommitSha;

            var git = new Git(new CommandRunner());
            await git.CloneAsync(CloneUrl, cloneDirectory, token);
            await git.CheckoutBranchAsync(branch);
            File.AppendAllText(Path.Combine(cloneDirectory, "README.md"), "an integration run\n");
            string addedFile = $"factory-integration-{Guid.NewGuid():N}.txt";
            File.WriteAllText(Path.Combine(cloneDirectory, addedFile), "an integration run\n");
            await git.CommitAsync("an integration commit", "Integration Bot", "integration@example.com");
            string headSha = await git.ReadHeadShaAsync();
            await git.PushAsync(branch, token);

            BranchInfo branchInfo = await GetBranch(api, branch);
            Assert.Equal(branch, branchInfo.Name);
            Assert.Equal(headSha, branchInfo.CommitSha);

            (string authorName, string authorEmail) = await GetCommitAuthor(api, headSha);
            Assert.Equal("Integration Bot", authorName);
            Assert.Equal("integration@example.com", authorEmail);

            Assert.Equal(mainBefore, (await GetBranch(api, "main")).CommitSha);

            IReadOnlyList<string> changed = await git.ListChangedFilesAsync("main");
            Assert.Equal(2, changed.Count);
            Assert.Contains("README.md", changed);
            Assert.Contains(addedFile, changed);
        }
        finally
        {
            Directory.Delete(cloneDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ASecondClonePushingADifferentCommitToTheSameBranchGetsTheRefusedPush()
    {
        string token = RequireToken();
        string branch = UniqueBranch();
        string firstClone = FreshCloneDirectory();
        string secondClone = FreshCloneDirectory();
        try
        {
            var first = new Git(new CommandRunner());
            await first.CloneAsync(CloneUrl, firstClone, token);
            await first.CheckoutBranchAsync(branch);
            File.WriteAllText(Path.Combine(firstClone, "factory-integration-first.txt"), "first\n");
            await first.CommitAsync("the first clone commit", "Integration Bot", "integration@example.com");
            await first.PushAsync(branch, token);

            var second = new Git(new CommandRunner());
            await second.CloneAsync(CloneUrl, secondClone, token);
            await second.CheckoutBranchAsync(branch);
            File.WriteAllText(Path.Combine(secondClone, "factory-integration-second.txt"), "second\n");
            await second.CommitAsync("the second clone commit", "Integration Bot", "integration@example.com");

            GitException exception = await Assert.ThrowsAsync<GitException>(() => second.PushAsync(branch, token));
            Assert.Contains("[rejected]", exception.Message);
        }
        finally
        {
            Directory.Delete(firstClone, recursive: true);
            Directory.Delete(secondClone, recursive: true);
        }
    }

    [Fact]
    public async Task APushWithAnInvalidTokenGetsTheFailedAuthentication()
    {
        string token = RequireToken();
        string branch = UniqueBranch();
        string cloneDirectory = FreshCloneDirectory();
        try
        {
            var git = new Git(new CommandRunner());
            await git.CloneAsync(CloneUrl, cloneDirectory, token);
            await git.CheckoutBranchAsync(branch);
            File.WriteAllText(Path.Combine(cloneDirectory, "factory-integration-auth.txt"), "auth\n");
            await git.CommitAsync("a commit to push", "Integration Bot", "integration@example.com");

            GitException exception = await Assert.ThrowsAsync<GitException>(() => git.PushAsync(branch, "invalid"));
            Assert.Contains("Authentication failed", exception.Message);
        }
        finally
        {
            Directory.Delete(cloneDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AnUnknownBaseRevisionGetsGitStandardError()
    {
        string token = RequireToken();
        string cloneDirectory = FreshCloneDirectory();
        try
        {
            var git = new Git(new CommandRunner());
            await git.CloneAsync(CloneUrl, cloneDirectory, token);

            GitException exception = await Assert.ThrowsAsync<GitException>(() => git.ListChangedFilesAsync("no-such-base"));
            Assert.Contains("fatal: ambiguous argument 'no-such-base': unknown revision or path not in the working tree.", exception.Message);
        }
        finally
        {
            Directory.Delete(cloneDirectory, recursive: true);
        }
    }

    private sealed record BranchInfo(string Name, string CommitSha);

    private static string RequireToken()
    {
        string token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN") ?? "";
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN must be set");
        return token;
    }

    private static string UniqueBranch() => $"factory/integration-{Guid.NewGuid():N}";

    private static string FreshCloneDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"factory-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static HttpClient ApiClient(string token)
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("factory-integration-suite");
        return client;
    }

    private static async Task<BranchInfo> GetBranch(HttpClient api, string name)
    {
        string json = await api.GetStringAsync($"https://api.github.com/repos/{Repo}/branches/{Uri.EscapeDataString(name)}");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return new BranchInfo(
            root.GetProperty("name").GetString() ?? "",
            root.GetProperty("commit").GetProperty("sha").GetString() ?? "");
    }

    private static async Task<(string Name, string Email)> GetCommitAuthor(HttpClient api, string sha)
    {
        string json = await api.GetStringAsync($"https://api.github.com/repos/{Repo}/commits/{sha}");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement author = document.RootElement.GetProperty("commit").GetProperty("author");
        return (author.GetProperty("name").GetString() ?? "", author.GetProperty("email").GetString() ?? "");
    }
}
