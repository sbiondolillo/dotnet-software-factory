using Core;
using Xunit;

namespace Integration;

public sealed class GitIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";
    private static readonly string Remote = $"https://github.com/{Owner}/{Repo}.git";

    [Fact]
    public async Task TheFullWorkflowPushesAndGithubReportsIt()
    {
        string token = RequireToken();
        string runId = Guid.NewGuid().ToString("N");
        string branch = $"factory/{runId}";
        string cloneDirectory = FreshCloneDirectory();
        try
        {
            var github = new GitHubApiClient(token);
            string mainShaBefore = (await github.GetBranchAsync(Owner, Repo, "main")).Sha;

            var git = new Git(new CommandRunner());
            await git.CloneAsync(Remote, cloneDirectory, token);
            await git.CheckoutBranchAsync(branch);

            File.WriteAllText(Path.Combine(cloneDirectory, "README.md"), $"factory edit {runId}\n");
            string addedFile = $"factory-{runId}.txt";
            File.WriteAllText(Path.Combine(cloneDirectory, addedFile), $"{runId}\n");

            string authorName = $"Factory Runner {runId[..8]}";
            string authorEmail = $"{runId[..8]}@factory.example.com";
            await git.CommitAllAsync($"factory edit {runId}", authorName, authorEmail);
            string headSha = await git.ReadHeadShaAsync();
            await git.PushAsync(branch);

            GitHubApiClient.BranchInfo branchInfo = await github.GetBranchAsync(Owner, Repo, branch);
            Assert.Equal(headSha, branchInfo.Sha);
            Assert.Equal(authorName, branchInfo.AuthorName);
            Assert.Equal(authorEmail, branchInfo.AuthorEmail);

            Assert.Equal(mainShaBefore, (await github.GetBranchAsync(Owner, Repo, "main")).Sha);

            string[] changed = (await git.ListChangedFilesAsync("main")).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(["README.md", addedFile], changed);
        }
        finally
        {
            DeleteDirectory(cloneDirectory);
        }
    }

    [Fact]
    public async Task ASecondPushToTheSameBranchIsRefused()
    {
        string token = RequireToken();
        string runId = Guid.NewGuid().ToString("N");
        string branch = $"factory/{runId}";
        string firstClone = FreshCloneDirectory();
        string secondClone = FreshCloneDirectory();
        try
        {
            var first = new Git(new CommandRunner());
            await first.CloneAsync(Remote, firstClone, token);
            await first.CheckoutBranchAsync(branch);
            File.WriteAllText(Path.Combine(firstClone, $"factory-{runId}.txt"), "first\n");
            await first.CommitAllAsync("first push", "Factory First", "first@factory.example.com");
            await first.PushAsync(branch);

            var second = new Git(new CommandRunner());
            await second.CloneAsync(Remote, secondClone, token);
            await second.CheckoutBranchAsync(branch);
            File.WriteAllText(Path.Combine(secondClone, $"second-{runId}.txt"), "second\n");
            await second.CommitAllAsync("second push", "Factory Second", "second@factory.example.com");

            var exception = await Assert.ThrowsAsync<GitCommandException>(() => second.PushAsync(branch));
            Assert.Contains("rejected", exception.Message);
        }
        finally
        {
            DeleteDirectory(firstClone);
            DeleteDirectory(secondClone);
        }
    }

    [Fact]
    public async Task AnInvalidTokenFailsAuthentication()
    {
        string token = RequireToken();
        string runId = Guid.NewGuid().ToString("N");
        string branch = $"factory/{runId}";
        string validClone = FreshCloneDirectory();
        string invalidClone = FreshCloneDirectory();
        try
        {
            var valid = new Git(new CommandRunner());
            await valid.CloneAsync(Remote, validClone, token);

            var git = new Git(new CommandRunner());
            await git.CloneAsync(Remote, invalidClone, "invalid");
            await git.CheckoutBranchAsync(branch);
            File.WriteAllText(Path.Combine(invalidClone, $"factory-{runId}.txt"), "invalid\n");
            await git.CommitAllAsync("invalid token push", "Factory Invalid", "invalid@factory.example.com");

            var exception = await Assert.ThrowsAsync<GitCommandException>(() => git.PushAsync(branch));
            Assert.Contains("Authentication failed", exception.Message);
        }
        finally
        {
            DeleteDirectory(validClone);
            DeleteDirectory(invalidClone);
        }
    }

    [Fact]
    public async Task AnUnknownBaseRevisionThrows()
    {
        string token = RequireToken();
        string cloneDirectory = FreshCloneDirectory();
        try
        {
            var git = new Git(new CommandRunner());
            await git.CloneAsync(Remote, cloneDirectory, token);

            var exception = await Assert.ThrowsAsync<GitCommandException>(() => git.ListChangedFilesAsync("no-such-base"));
            Assert.Contains("no-such-base", exception.Message);
        }
        finally
        {
            DeleteDirectory(cloneDirectory);
        }
    }

    private static string RequireToken()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        return token is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("FACTORY_GITHUB_TOKEN is not set");
    }

    private static string FreshCloneDirectory() => Path.Combine(Path.GetTempPath(), $"factory-git-{Guid.NewGuid():N}");

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
