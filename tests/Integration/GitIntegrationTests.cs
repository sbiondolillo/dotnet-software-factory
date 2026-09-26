using Core;
using Xunit;

namespace Integration;

/// <summary>Runs <see cref="Git"/> against the real git binary and the shared scratch repo on GitHub.</summary>
public sealed class GitIntegrationTests
{
    private const string RepositoryUrl = "https://github.com/sbiondolillo/factory-scratch.git";
    private const string AuthorName = "Factory Integration";
    private const string AuthorEmail = "factory-integration@example.com";
    private const string NewFile = "factory-note.txt";

    [Fact]
    public async Task AFactoryRunPushesABranchThatGitHubReportsAtTheHeadShaTheGitRead()
    {
        string token = RequireToken();
        GitHubApi api = new(token);
        BranchHead mainBefore = await api.ReadBranchHeadAsync("main");

        DirectoryInfo clone = Directory.CreateTempSubdirectory("factory-clone-");
        try
        {
            Git git = new(new CommandRunner());
            string branch = $"factory/it-{Guid.NewGuid():N}";

            await git.CloneAsync(RepositoryUrl, token, clone.FullName);
            await git.CheckoutBranchAsync(clone.FullName, branch);
            File.AppendAllText(Path.Combine(clone.FullName, "README.md"), $"{branch} delivered the factory's work\n");
            File.WriteAllText(Path.Combine(clone.FullName, NewFile), "delivered by the factory\n");
            await git.CommitAllAsync(clone.FullName, "Deliver the factory's work", AuthorName, AuthorEmail);
            string headSha = await git.ReadHeadShaAsync(clone.FullName);
            await git.PushAsync(clone.FullName, branch, token);

            BranchHead pushed = await api.ReadBranchHeadAsync(branch);
            Assert.Equal(headSha, pushed.Sha);
            Assert.Equal(AuthorName, pushed.Author.Name);
            Assert.Equal(AuthorEmail, pushed.Author.Email);

            Assert.Equal(mainBefore.Sha, (await api.ReadBranchHeadAsync("main")).Sha);

            IReadOnlyList<string> changed = await git.ListChangedFilesAsync(clone.FullName, "main");
            Assert.Contains("README.md", changed);
            Assert.Contains(NewFile, changed);
        }
        finally
        {
            clone.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ASecondCloneThatPushesADifferentCommitToTheSameBranchNameGetsTheRefusedPush()
    {
        string token = RequireToken();
        Git git = new(new CommandRunner());
        string branch = $"factory/it-{Guid.NewGuid():N}";

        DirectoryInfo first = Directory.CreateTempSubdirectory("factory-clone-");
        try
        {
            await git.CloneAsync(RepositoryUrl, token, first.FullName);
            await git.CheckoutBranchAsync(first.FullName, branch);
            File.WriteAllText(Path.Combine(first.FullName, NewFile), "the first delivery\n");
            await git.CommitAllAsync(first.FullName, "The first delivery", AuthorName, AuthorEmail);
            await git.PushAsync(first.FullName, branch, token);

            DirectoryInfo second = Directory.CreateTempSubdirectory("factory-clone-");
            try
            {
                await git.CloneAsync(RepositoryUrl, token, second.FullName);
                await git.CheckoutBranchAsync(second.FullName, branch);
                File.WriteAllText(Path.Combine(second.FullName, NewFile), "a different delivery\n");
                await git.CommitAllAsync(second.FullName, "A different delivery", AuthorName, AuthorEmail);

                GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
                    () => git.PushAsync(second.FullName, branch, token));
                Assert.Contains("fetch first", exception.Message);
            }
            finally
            {
                second.Delete(recursive: true);
            }
        }
        finally
        {
            first.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task APushWithTheInvalidTokenGetsTheFailedAuthentication()
    {
        string token = RequireToken();
        Git git = new(new CommandRunner());
        DirectoryInfo clone = Directory.CreateTempSubdirectory("factory-clone-");
        try
        {
            string branch = $"factory/it-{Guid.NewGuid():N}";
            await git.CloneAsync(RepositoryUrl, token, clone.FullName);
            await git.CheckoutBranchAsync(clone.FullName, branch);
            File.WriteAllText(Path.Combine(clone.FullName, NewFile), "an invalid-token delivery\n");
            await git.CommitAllAsync(clone.FullName, "An invalid-token delivery", AuthorName, AuthorEmail);

            GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
                () => git.PushAsync(clone.FullName, branch, "invalid"));
            Assert.Contains("Authentication failed", exception.Message);
        }
        finally
        {
            clone.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ABaseOfNoSuchBaseGetsTheUnknownRevision()
    {
        string token = RequireToken();
        Git git = new(new CommandRunner());
        DirectoryInfo clone = Directory.CreateTempSubdirectory("factory-clone-");
        try
        {
            await git.CloneAsync(RepositoryUrl, token, clone.FullName);

            GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
                () => git.ListChangedFilesAsync(clone.FullName, "no-such-base"));
            Assert.Contains("unknown revision", exception.Message);
        }
        finally
        {
            clone.Delete(recursive: true);
        }
    }

    private static string RequireToken()
    {
        string token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN") ?? "";
        Assert.NotEmpty(token);
        return token;
    }
}
