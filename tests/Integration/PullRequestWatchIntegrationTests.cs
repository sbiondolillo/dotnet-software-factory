using System.Text;
using System.Text.Json;
using Core;
using Xunit;
using System.Linq;

namespace Integration;

/// <summary>Proves the canned answers of the pull request watch in tests/Specs are what GitHub gives.</summary>
public sealed class PullRequestWatchIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    private static GitHubClient Client()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    private static async Task<(string Branch, int Number)> OpenAsync(GitHubClient client, CancellationToken ct, string prefix = "watch-test/", string directory = "")
    {
        string branch = prefix + Guid.NewGuid().ToString("N");
        JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
        string sha = main.GetProperty("object").GetProperty("sha").GetString() ?? "";
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha }), ct);
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes(branch));
        await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{directory}{branch}.txt", JsonSerializer.Serialize(new { message = "add " + branch, content, branch }), ct);
        PullRequest pr = await new PullRequests(client).OpenAsync(Owner, Repo, branch, "main", "watch test " + branch, "body", ct);
        return (branch, pr.Number);
    }

    private static async Task CleanUpAsync(GitHubClient client, int number, string branch)
    {
        if (number is not 0)
        {
            await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{number}", "{\"state\":\"closed\"}");
        }

        await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}");
    }

    private static async Task CommentAsync(GitHubClient client, int number, string path, CancellationToken ct)
    {
        string sha = await new PullRequests(client).ReadHeadShaAsync(Owner, Repo, number, ct);
        string comment = JsonSerializer.Serialize(new { body = "Name the unit.", commit_id = sha, path, line = 1, side = "RIGHT" });
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/pulls/{number}/comments", comment, ct);
    }

    /// <summary>Waits up to 15 minutes for the watch-fixture workflow to put a check run named build on the head commit.</summary>
    private static async Task WaitUntilBuildCheckRunArrivesAsync(PullRequests pulls, int number, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(15);
        while (true)
        {
            string sha = await pulls.ReadHeadShaAsync(Owner, Repo, number, ct);
            IReadOnlyList<CheckRun> runs = await pulls.ReadCheckRunsAsync(Owner, Repo, sha, ct);
            if (runs.Any(r => r.Name is "build"))
                return;
            if (DateTime.UtcNow >= deadline)
                Assert.Fail("GitHub started no watch-fixture workflow run within 15 minutes");
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    [Theory]
    [InlineData("watch-green/", true, "success")]
    [InlineData("watch-red/", false, "failure")]
    public async Task AFixtureBranchGivesTheCannedCheckRunsAndTheWatchEndsGreenOrRed(string prefix, bool green, string conclusion)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string branch = "";
        int number = 0;
        try
        {
            (branch, number) = await OpenAsync(client, ct, prefix, "watch-fixture/");
            await CommentAsync(client, number, "watch-fixture/" + branch + ".txt", ct);
            var pulls = new PullRequests(client);
            await WaitUntilBuildCheckRunArrivesAsync(pulls, number, ct);
            var watch = new PullRequestWatch(pulls, new ReviewThreads(client));
            PullRequestWatchResult result = await watch.WatchAsync(Owner, Repo, number, Owner, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), ct);
            Assert.Equal(green, result.IsGreen);
            string sha = await pulls.ReadHeadShaAsync(Owner, Repo, number, ct);
            IReadOnlyList<CheckRun> runs = await pulls.ReadCheckRunsAsync(Owner, Repo, sha, ct);
            Assert.Contains(new CheckRun("build", "completed", conclusion), runs);
            Assert.Contains(new CheckRun("skipped", "completed", "skipped"), runs);
        }
        finally
        {
            await CleanUpAsync(client, number, branch);
        }
    }

    [Fact]
    public async Task TheHeadShaOfAPullRequestIsTheBranchHeadAndPullZeroGives404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string branch = "";
        int number = 0;
        try
        {
            (branch, number) = await OpenAsync(client, ct);
            JsonElement reference = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/{branch}", null, ct);
            string expected = reference.GetProperty("object").GetProperty("sha").GetString() ?? "";
            Assert.Equal(expected, await new PullRequests(client).ReadHeadShaAsync(Owner, Repo, number, ct));
            var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => new PullRequests(client).ReadHeadShaAsync(Owner, Repo, 0, ct));
            Assert.Equal(404, ex.StatusCode);
        }
        finally
        {
            await CleanUpAsync(client, number, branch);
        }
    }

    [Fact]
    public async Task APullRequestWithNoCheckRunAndTimeoutZeroIsRedAndAResolvedThreadLeavesTheResult()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string branch = "";
        int number = 0;
        try
        {
            (branch, number) = await OpenAsync(client, ct);
            var threads = new ReviewThreads(client);
            Assert.Empty(await threads.ReadReviewsAsync(Owner, Repo, number, ct));
            await CommentAsync(client, number, branch + ".txt", ct);
            var watch = new PullRequestWatch(new PullRequests(client), threads);
            PullRequestWatchResult result = await watch.WatchAsync(Owner, Repo, number, Owner, TimeSpan.Zero, TimeSpan.Zero, ct);
            Assert.False(result.IsGreen);
            ReviewThread thread = Assert.Single(result.Threads);
            Assert.Equal("Name the unit.", Assert.Single(thread.Comments).Body);

            await threads.ResolveAsync(thread.Id, ct);
            PullRequestWatchResult after = await watch.WatchAsync(Owner, Repo, number, Owner, TimeSpan.Zero, TimeSpan.Zero, ct);
            Assert.Empty(after.Threads);
            Assert.Contains(await threads.ReadReviewsAsync(Owner, Repo, number, ct), r => r.Author == Owner);
        }
        finally
        {
            await CleanUpAsync(client, number, branch);
        }
    }
}
