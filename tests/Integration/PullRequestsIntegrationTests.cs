using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned pull request and check-run answers in tests/Specs are what GitHub gives.</summary>
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

    private static async Task<(string Branch, string Sha)> CreateBranchAsync(GitHubClient client, CancellationToken ct)
    {
        JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
        string baseSha = main.GetProperty("object").GetProperty("sha").GetString()!;
        string branch = "pr-test/" + Guid.NewGuid();
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha = baseSha }), ct);
        JsonElement file = await client.SendAsync(
            HttpMethod.Put,
            $"/repos/{Owner}/{Repo}/contents/pr-test-{Guid.NewGuid()}.txt",
            JsonSerializer.Serialize(new { message = "pr test", content = Convert.ToBase64String("x"u8.ToArray()), branch }),
            ct);
        return (branch, file.GetProperty("commit").GetProperty("sha").GetString()!);
    }

    private static async Task CleanupAsync(GitHubClient client, List<int> prs, List<string> branches)
    {
        foreach (int n in prs)
        {
            await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{n}", "{\"state\":\"closed\"}");
        }

        foreach (string b in branches)
        {
            await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{b}");
        }
    }

    [Fact]
    public async Task OpeningTwiceGivesTheSamePullRequestAndEachBranchGivesItsOwn()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        var prs = new PullRequests(client);
        var opened = new List<int>();
        var branches = new List<string>();
        try
        {
            (string first, string firstSha) = await CreateBranchAsync(client, ct);
            branches.Add(first);
            PullRequest one = await prs.OpenAsync(Owner, Repo, first, "main", "pr test", "body", ct);
            opened.Add(one.Number);
            PullRequest again = await prs.OpenAsync(Owner, Repo, first, "main", "pr test", "body", ct);
            Assert.Equal(one, again);

            (string second, _) = await CreateBranchAsync(client, ct);
            branches.Add(second);
            PullRequest two = await prs.OpenAsync(Owner, Repo, second, "main", "pr test 2", "body", ct);
            opened.Add(two.Number);
            Assert.NotEqual(one.Number, two.Number);
            Assert.Equal(one, await prs.OpenAsync(Owner, Repo, first, "main", "pr test", "body", ct));

            Assert.Empty(await prs.ReadCheckRunsAsync(Owner, Repo, firstSha, ct));
        }
        finally
        {
            await CleanupAsync(client, opened, branches);
        }
    }

    [Fact]
    public async Task TheFixtureCommitHasOneCompletedSuccessfulCheckRun()
    {
        CheckRun run = Assert.Single(await new PullRequests(Client()).ReadCheckRunsAsync(Owner, Repo, FixtureSha, TestContext.Current.CancellationToken));
        Assert.Equal(new CheckRun("fixture", "completed", "success"), run);
    }

    [Fact]
    public async Task ReadingAnUnknownCommitThrowsWith422()
    {
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() =>
            new PullRequests(Client()).ReadCheckRunsAsync(Owner, Repo, new string('0', 40), TestContext.Current.CancellationToken));
        Assert.Equal(422, ex.StatusCode);
    }
}
