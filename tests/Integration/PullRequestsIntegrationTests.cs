using System.Text;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned pull request and check-run answers in tests/Specs are what GitHub gives.</summary>
[Collection(GitHubWrites.Name)]
public sealed class PullRequestsIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";
    private const string FixtureSha = "4e24cdc9f280411706bb0de5fabcbb06e6626348";

    private static GitHubClient Client()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    private static async Task<string> CreateBranchAsync(GitHubClient client, CancellationToken ct)
    {
        string branch = "pr-test/" + Guid.NewGuid().ToString("N");
        JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
        string sha = main.GetProperty("object").GetProperty("sha").GetString() ?? "";
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha }), ct);
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes(branch));
        await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{branch}.txt", JsonSerializer.Serialize(new { message = "add " + branch, content, branch }), ct);
        return branch;
    }

    private static async Task<string> HeadShaAsync(GitHubClient client, string branch, CancellationToken ct)
    {
        JsonElement json = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/{branch}", null, ct);
        return json.GetProperty("object").GetProperty("sha").GetString() ?? "";
    }

    private static async Task CleanUpAsync(GitHubClient client, List<int> pulls, List<string> branches)
    {
        foreach (int number in pulls)
        {
            await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{number}", "{\"state\":\"closed\"}");
        }

        foreach (string branch in branches)
        {
            await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}");
        }
    }

    [Fact]
    public async Task OpeningTwiceOnOneBranchGivesTheSamePullRequestAndTwoBranchesGiveTwo()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        var pulls = new List<int>();
        var branches = new List<string>();
        try
        {
            var sut = new PullRequests(client);
            string first = await CreateBranchAsync(client, ct);
            branches.Add(first);
            PullRequest a = await sut.OpenAsync(Owner, Repo, first, "main", "test " + first, "body", ct);
            pulls.Add(a.Number);
            PullRequest again = await sut.OpenAsync(Owner, Repo, first, "main", "test " + first, "body", ct);
            Assert.Equal(a, again);

            string second = await CreateBranchAsync(client, ct);
            branches.Add(second);
            PullRequest b = await sut.OpenAsync(Owner, Repo, second, "main", "test " + second, "body", ct);
            pulls.Add(b.Number);
            Assert.NotEqual(a.Number, b.Number);
            Assert.Equal(a, await sut.OpenAsync(Owner, Repo, first, "main", "test", "body", ct));
        }
        finally
        {
            await CleanUpAsync(client, pulls, branches);
        }
    }

    [Fact]
    public async Task ACompletedCheckRunReadsWithNameStatusAndConclusion()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IReadOnlyList<CheckRun> runs = await new PullRequests(Client()).ReadCheckRunsAsync(Owner, Repo, FixtureSha, ct);
        Assert.Equal(new CheckRun("fixture", "completed", "success"), Assert.Single(runs));
    }

    [Fact]
    public async Task ACommitWithNoCheckRunReadsAsAnEmptyList()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        var branches = new List<string>();
        try
        {
            string branch = await CreateBranchAsync(client, ct);
            branches.Add(branch);
            string sha = await HeadShaAsync(client, branch, ct);
            Assert.Empty(await new PullRequests(client).ReadCheckRunsAsync(Owner, Repo, sha, ct));
        }
        finally
        {
            await CleanUpAsync(client, [], branches);
        }
    }

    [Fact]
    public async Task AnUnknownCommitThrowsWith422()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            new PullRequests(Client()).ReadCheckRunsAsync(Owner, Repo, new string('0', 40), ct));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task AnOpenFromAHeadGitHubLacksThrowsWith422()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string head = "pr-test/" + Guid.NewGuid().ToString("N");
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            new PullRequests(client).OpenAsync(Owner, Repo, head, "main", "test " + head, "body", ct));
        Assert.Equal(422, ex.StatusCode);
        GitHubRequestException gone = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/{head}", null, ct));
        Assert.Equal(404, gone.StatusCode);
    }
}
