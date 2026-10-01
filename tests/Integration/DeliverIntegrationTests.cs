using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned GitHub answers behind the Deliver scenarios are what GitHub gives.</summary>
public sealed class DeliverIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    [Fact]
    public async Task DeliveringOpensAPullRequestThatClosesTheIssueAndCommentsTheAddress()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        var client = new GitHubClient(new HttpClient(), token);
        var git = new Git(new CommandRunner());
        string runId = Guid.NewGuid().ToString("N");
        string branch = "factory/" + runId;
        int pullNumber = 0;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-deliver-");
        try
        {
            JsonElement issue = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/issues", JsonSerializer.Serialize(new { title = "Deliver integration " + runId }), ct);
            int issueNumber = issue.GetProperty("number").GetInt32();

            await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, ct);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, runId + ".txt"), runId, ct);
            await git.CommitAllAsync(directory.FullName, "Deliver integration " + runId, "Factory Integration Test", "factory-integration@example.com", ct);

            var deliver = new Deliver(git, new PullRequests(client), client);
            PullRequest pull = await deliver.DeliverAsync(directory.FullName, runId, Owner, Repo, "main", issueNumber, "Deliver integration " + runId, token, ct);
            pullNumber = pull.Number;

            JsonElement read = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/pulls/{pull.Number}", null, ct);
            Assert.StartsWith($"Closes #{issueNumber}", read.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal(pull.HtmlUrl, read.GetProperty("html_url").GetString());

            JsonElement comments = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/issues/{issueNumber}/comments", null, ct);
            JsonElement only = Assert.Single(comments.EnumerateArray().ToList());
            Assert.Contains(pull.HtmlUrl, only.GetProperty("body").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (pullNumber is not 0)
            {
                await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{pullNumber}", "{\"state\":\"closed\"}", CancellationToken.None);
            }

            await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}", null, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AnOpenFromAHeadGitHubLacksGetsTheStatusCode422()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        var client = new GitHubClient(new HttpClient(), token);
        string head = "factory/missing-" + Guid.NewGuid().ToString("N");

        GitHubRequestException exception = await Assert.ThrowsAsync<GitHubRequestException>(
            () => new PullRequests(client).OpenAsync(Owner, Repo, head, "main", "Missing head " + head, "Closes #1\n", ct));

        Assert.Equal(422, exception.StatusCode);
    }
}
