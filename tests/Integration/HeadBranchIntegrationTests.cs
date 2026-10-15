using System.Text;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned head-branch read and the canned checkout of an origin branch in tests/Specs are what GitHub and git give.</summary>
[Collection(GitHubWrites.Name)]
public sealed class HeadBranchIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    private static string Token
    {
        get
        {
            string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
            Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
            return token;
        }
    }

    private static async Task<string> CreateBranchAsync(GitHubClient client, Cleanup cleanup, CancellationToken ct)
    {
        string branch = "head-branch-test/" + Guid.NewGuid().ToString("N");
        JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
        string sha = main.GetProperty("object").GetProperty("sha").GetString() ?? "";
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha }), ct);
        cleanup.Add($"delete the branch {branch}", () => client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}"));
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes(branch));
        await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{branch}.txt", JsonSerializer.Serialize(new { message = "add " + branch, content, branch }), ct);
        return branch;
    }

    [Fact]
    public async Task TheHeadBranchOfAnOpenPullRequestIsTheBranchItWasOpenedFrom()
    {
        var client = new GitHubClient(new HttpClient(), Token);
        Assert.Equal(ScratchFixtures.EmptyBranch, await new PullRequests(client).ReadHeadBranchAsync(Owner, Repo, ScratchFixtures.EmptyPull, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheReadOfAPullRequestThatDoesNotExistThrowsTheGitHubRequestException()
    {
        var client = new GitHubClient(new HttpClient(), Token);
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(
            () => new PullRequests(client).ReadHeadBranchAsync(Owner, Repo, 999999999, TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task TheCheckoutOfABranchThatOriginHoldsGivesALocalBranchAtItsHeadCommit()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string token = Token;
        var client = new GitHubClient(new HttpClient(), token);
        await Cleanup.RunAsync(async cleanup =>
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-checkout-integration-");
            cleanup.Add($"delete the directory {directory.FullName}", () => directory.Delete(recursive: true));
            string branch = await CreateBranchAsync(client, cleanup, ct);
            var sut = new PullRequests(client);
            PullRequest opened = await sut.OpenAsync(Owner, Repo, branch, "main", "test " + branch, "body", ct);
            cleanup.Add($"close the pull request #{opened.Number}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{opened.Number}", "{\"state\":\"closed\"}"));
            string headSha = await sut.ReadHeadShaAsync(Owner, Repo, opened.Number, ct);

            var git = new Git(new CommandRunner());
            await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, ct);
            await git.CheckoutExistingBranchAsync(branch, directory.FullName, ct);
            Assert.Equal(headSha, await git.ReadHeadShaAsync(directory.FullName, ct));
        });
    }

    [Fact]
    public async Task TheCheckoutOfABranchThatOriginLacksExitsWithTheCodeOne()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await Cleanup.RunAsync(async cleanup =>
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-checkout-integration-");
            cleanup.Add($"delete the directory {directory.FullName}", () => directory.Delete(recursive: true));
            var git = new Git(new CommandRunner());
            await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", Token, directory.FullName, ct);
            GitCommandException ex = await Assert.ThrowsAsync<GitCommandException>(
                () => git.CheckoutExistingBranchAsync("no-such-branch-" + Guid.NewGuid().ToString("N"), directory.FullName, ct));
            Assert.Contains("did not match any file(s) known to git", ex.Message, StringComparison.Ordinal);
        });
    }
}
