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

    private static async Task<string> CreateBranchAsync(GitHubClient client, Cleanup cleanup, CancellationToken ct)
    {
        string branch = "pr-test/" + Guid.NewGuid().ToString("N");
        JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
        string sha = main.GetProperty("object").GetProperty("sha").GetString() ?? "";
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha }), ct);
        cleanup.Add($"delete the branch {branch}", () => client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}"));
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes(branch));
        await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{branch}.txt", JsonSerializer.Serialize(new { message = "add " + branch, content, branch }), ct);
        return branch;
    }

    private static async Task<string> HeadShaAsync(GitHubClient client, string branch, CancellationToken ct)
    {
        JsonElement json = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/{branch}", null, ct);
        return json.GetProperty("object").GetProperty("sha").GetString() ?? "";
    }

    [Fact]
    public async Task OpeningTwiceOnOneBranchGivesTheSamePullRequestAndTwoBranchesGiveTwo()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        await Cleanup.RunAsync(async cleanup =>
        {
            var sut = new PullRequests(client);
            string first = await CreateBranchAsync(client, cleanup, ct);
            PullRequest a = await sut.OpenAsync(new RepoName(Owner, Repo), first, "main", "test " + first, "body", ct);
            cleanup.Add($"close the pull request #{a.Number}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{a.Number}", "{\"state\":\"closed\"}"));
            PullRequest again = await sut.OpenAsync(new RepoName(Owner, Repo), first, "main", "test " + first, "body", ct);
            Assert.Equal(a, again);

            PullRequest b = await sut.OpenAsync(new RepoName(Owner, Repo), ScratchFixtures.EmptyBranch, "main", "test", "body", ct);
            Assert.Equal(ScratchFixtures.EmptyPull, b.Number);
            Assert.NotEqual(a.Number, b.Number);
            Assert.Equal(a, await sut.OpenAsync(new RepoName(Owner, Repo), first, "main", "test", "body", ct));
        });
    }

    [Fact]
    public async Task ACompletedCheckRunReadsWithNameStatusAndConclusion()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IReadOnlyList<CheckRun> runs = await new PullRequests(Client()).ReadCheckRunsAsync(new RepoName(Owner, Repo), FixtureSha, ct);
        Assert.Equal(new CheckRun("fixture", "completed", "success"), Assert.Single(runs));
    }

    [Fact]
    public async Task ACommitWithNoCheckRunReadsAsAnEmptyList()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        await Cleanup.RunAsync(async cleanup =>
        {
            string branch = await CreateBranchAsync(client, cleanup, ct);
            string sha = await HeadShaAsync(client, branch, ct);
            Assert.Empty(await new PullRequests(client).ReadCheckRunsAsync(new RepoName(Owner, Repo), sha, ct));
        });
    }

    [Fact]
    public async Task AnUnknownCommitThrowsWith422()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            new PullRequests(Client()).ReadCheckRunsAsync(new RepoName(Owner, Repo), new string('0', 40), ct));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task AnOpenFromAHeadGitHubLacksThrowsWith422()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string head = "pr-test/" + Guid.NewGuid().ToString("N");
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            new PullRequests(client).OpenAsync(new RepoName(Owner, Repo), head, "main", "test " + head, "body", ct));
        Assert.Equal(422, ex.StatusCode);
        GitHubRequestException gone = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/{head}", null, ct));
        Assert.Equal(404, gone.StatusCode);
    }

    /// <summary>
    /// GitHub lists the closing issue of a new pull request late: in 6 of 8 opens on 2026-11-11 the first read gave an empty
    /// list, and a read about 1.2 seconds after the open listed the issue. So the test reads each second, up to 30 times.
    /// </summary>
    [Fact]
    public async Task AFixesLineInTheBodyMakesItsIssueTheClosingIssue()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        await Cleanup.RunAsync(async cleanup =>
        {
            JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/issues", JsonSerializer.Serialize(new { title = "closing issue test" }), ct);
            int issue = created.GetProperty("number").GetInt32();
            cleanup.Add($"close the issue #{issue}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/issues/{issue}", "{\"state\":\"closed\"}"));
            string branch = await CreateBranchAsync(client, cleanup, ct);
            var sut = new PullRequests(client);
            PullRequest pull = await sut.OpenAsync(new RepoName(Owner, Repo), branch, "main", "test " + branch, $"Fixes #{issue}", ct);
            cleanup.Add($"close the pull request #{pull.Number}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{pull.Number}", "{\"state\":\"closed\"}"));

            var number = new IssueNumber(new RepoName(Owner, Repo), pull.Number);
            IssueNumber? closing = await sut.ReadClosingIssueAsync(number, ct);
            for (int read = 1; read < 30 && closing is null; read++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                closing = await sut.ReadClosingIssueAsync(number, ct);
            }

            Assert.Equal(new IssueNumber(new RepoName(Owner, Repo), issue), closing);
        });
    }
}
