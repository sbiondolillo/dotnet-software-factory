using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the git answers <c>tests/Specs</c> canned still hold against the real GitHub and a real <c>git</c>.</summary>
[Collection(GitHubWrites.Name)]
public sealed class GitIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    private static string Token => Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN")
        ?? throw new InvalidOperationException("FACTORY_GITHUB_TOKEN is not set");

    [Fact]
    public async Task ClonePushAndBranchApiAgreeOnTheHeadShaAndTheAuthorAndTheChangedFiles()
    {
        string token = Token;
        using HttpClient http = NewGitHubClient(token);

        string branch = $"factory-integration-{Guid.NewGuid():N}";
        string newFile = $"factory-integration-{Guid.NewGuid():N}.txt";
        string authorName = "Factory Integration Test";
        string authorEmail = "factory-integration@example.com";

        string mainShaBefore = await ReadBranchShaAsync(http, "main");

        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-git-integration-");
        var git = new Git(new CommandRunner());

        try
        {
            await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, TestContext.Current.CancellationToken);
            await git.CheckoutBranchAsync(branch, directory.FullName, TestContext.Current.CancellationToken);

            await File.AppendAllTextAsync(Path.Combine(directory.FullName, "README.md"), $"\nfactory integration {branch}\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, newFile), "factory integration test file\n", TestContext.Current.CancellationToken);

            await git.CommitAllAsync(directory.FullName, $"Factory integration test {branch}", authorName, authorEmail, TestContext.Current.CancellationToken);
            string headSha = await git.ReadHeadShaAsync(directory.FullName, TestContext.Current.CancellationToken);
            await git.PushAsync(branch, directory.FullName, token, TestContext.Current.CancellationToken);

            BranchInfo branchInfo = await ReadBranchAsync(http, branch);
            Assert.Equal(headSha, branchInfo.Commit.Sha);
            Assert.Equal(authorName, branchInfo.Commit.Commit.Author.Name);
            Assert.Equal(authorEmail, branchInfo.Commit.Commit.Author.Email);

            string mainShaAfter = await ReadBranchShaAsync(http, "main");
            Assert.Equal(mainShaBefore, mainShaAfter);

            string[] changedFiles = await git.ListChangedFilesAsync("main", directory.FullName, TestContext.Current.CancellationToken);
            Assert.Contains("README.md", changedFiles);
            Assert.Contains(newFile, changedFiles);
        }
        finally
        {
            // Remove the branch the push created, so a run leaves the repo with the branches it held before.
            // No branch means a 422, which DeleteAsync does not throw, so a test that failed before
            // its push reports that first failure.
            await http.DeleteAsync($"repos/{Owner}/{Repo}/git/refs/heads/{branch}", TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task APushWithAnInvalidTokenFailsAuthentication()
    {
        string token = Token;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-git-integration-");
        var git = new Git(new CommandRunner());
        string branch = $"factory-integration-{Guid.NewGuid():N}";

        await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, TestContext.Current.CancellationToken);
        await git.CheckoutBranchAsync(branch, directory.FullName, TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(Path.Combine(directory.FullName, "README.md"), $"\nfactory integration {branch}\n", TestContext.Current.CancellationToken);
        await git.CommitAllAsync(directory.FullName, $"Factory integration test {branch}", "Factory Integration Test", "factory-integration@example.com", TestContext.Current.CancellationToken);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.PushAsync(branch, directory.FullName, "invalid", TestContext.Current.CancellationToken));

        Assert.Contains("push", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Authentication failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedFilesReadAgainstAnUnknownBaseGetsGitsUnknownRevisionAnswer()
    {
        string token = Token;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-git-integration-");
        var git = new Git(new CommandRunner());

        await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, TestContext.Current.CancellationToken);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ListChangedFilesAsync("no-such-base", directory.FullName, TestContext.Current.CancellationToken));

        Assert.Contains("diff", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unknown revision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiffAgainstABaseHoldsOneSectionPerTrackedFileAndNoneForAnUntrackedFile()
    {
        (DirectoryInfo directory, string baseSha, Git git) = await NewLocalRepoAsync();
        await File.AppendAllTextAsync(Path.Combine(directory.FullName, "a.txt"), "two\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "b.txt"), "new\n", TestContext.Current.CancellationToken);
        await git.CommitAllAsync(directory.FullName, "second", "Factory Integration Test", "factory-integration@example.com", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "c.txt"), "untracked\n", TestContext.Current.CancellationToken);

        string diff = await git.ReadDiffAsync(baseSha, directory.FullName, TestContext.Current.CancellationToken);

        Assert.Contains("diff --git a/a.txt b/a.txt", diff, StringComparison.Ordinal);
        Assert.Contains("+two", diff, StringComparison.Ordinal);
        int bSection = diff.IndexOf("diff --git a/b.txt b/b.txt", StringComparison.Ordinal);
        Assert.True(bSection >= 0);
        Assert.Contains("new file mode 100644", diff[bSection..], StringComparison.Ordinal);
        Assert.DoesNotContain("c.txt", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiffWithHeadAtTheBaseIsEmpty()
    {
        (DirectoryInfo directory, string baseSha, Git git) = await NewLocalRepoAsync();

        string diff = await git.ReadDiffAsync(baseSha, directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal("", diff);
    }

    [Fact]
    public async Task ADiffAgainstAnUnknownBaseGetsGitsBadObjectAnswer()
    {
        (DirectoryInfo directory, _, Git git) = await NewLocalRepoAsync();
        string unknown = new('0', 40);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ReadDiffAsync(unknown, directory.FullName, TestContext.Current.CancellationToken));

        Assert.Contains("diff", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"fatal: bad object {unknown}", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<(DirectoryInfo Directory, string BaseSha, Git Git)> NewLocalRepoAsync()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-git-diff-");
        var runner = new CommandRunner();
        var git = new Git(runner);
        CommandResult init = await runner.RunAsync(
            new CommandSpec { Command = "git", Arguments = ["init"], WorkingDirectory = directory.FullName },
            TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "a.txt"), "one\n", TestContext.Current.CancellationToken);
        await git.CommitAllAsync(directory.FullName, "first", "Factory Integration Test", "factory-integration@example.com", TestContext.Current.CancellationToken);
        string baseSha = await git.ReadHeadShaAsync(directory.FullName, TestContext.Current.CancellationToken);
        return (directory, baseSha, git);
    }

    private static HttpClient NewGitHubClient(string token)
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("factory-integration-tests", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<string> ReadBranchShaAsync(HttpClient http, string branch)
        => (await ReadBranchAsync(http, branch)).Commit.Sha;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static async Task<BranchInfo> ReadBranchAsync(HttpClient http, string branch)
    {
        HttpResponseMessage response = await http.GetAsync($"repos/{Owner}/{Repo}/branches/{branch}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BranchInfo>(JsonOptions)
            ?? throw new InvalidOperationException($"GitHub's branch API returned no body for {branch}");
    }

    public sealed class BranchInfo
    {
        public required BranchCommit Commit { get; init; }
    }

    public sealed class BranchCommit
    {
        public required string Sha { get; init; }

        public required CommitDetail Commit { get; init; }
    }

    public sealed class CommitDetail
    {
        public required CommitAuthor Author { get; init; }
    }

    public sealed class CommitAuthor
    {
        public required string Name { get; init; }

        public required string Email { get; init; }
    }
}
