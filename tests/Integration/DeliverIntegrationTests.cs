using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned git, pull request and comment answers in tests/Specs/Steps/DeliverSteps.cs are what git and GitHub give.</summary>
public sealed class DeliverIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    [Fact]
    public async Task DeliverPushesOpensAPullRequestAndCommentsOnTheIssue()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN")
            ?? throw new InvalidOperationException("FACTORY_GITHUB_TOKEN is not set");
        using var http = new HttpClient();
        var client = new GitHubClient(http, token);
        string runId = Guid.NewGuid().ToString("N")[..8];
        string branch = "factory/" + runId;
        string title = "Deliver integration " + runId;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-deliver-integration-");
        int issue = 0;
        int pull = 0;
        try
        {
            JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/issues", JsonSerializer.Serialize(new { title }), ct);
            issue = created.GetProperty("number").GetInt32();

            var git = new Git(new CommandRunner());
            await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, ct);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, $"deliver-{runId}.txt"), runId, ct);
            await git.CommitAllAsync(directory.FullName, "Deliver integration " + runId, "Factory Integration Test", "factory-integration@example.com", ct);

            var pullRequests = new PullRequests(client);
            var deliver = new Deliver(git, pullRequests, client);
            PullRequest result = await deliver.RunAsync(directory.FullName, runId, Owner, Repo, "main", issue, title, token, ct);
            pull = result.Number;

            JsonElement pr = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/pulls/{pull}", null, ct);
            Assert.Equal(title, pr.GetProperty("title").GetString());
            Assert.StartsWith($"Closes #{issue}", pr.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal(result.HtmlUrl, pr.GetProperty("html_url").GetString());

            JsonElement comments = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/issues/{issue}/comments", null, ct);
            JsonElement only = Assert.Single(comments.EnumerateArray().ToList());
            Assert.Contains(result.HtmlUrl, only.GetProperty("body").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (pull is not 0)
            {
                await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{pull}", "{\"state\":\"closed\"}", TestContext.Current.CancellationToken);
            }

            if (issue is not 0)
            {
                await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/issues/{issue}", "{\"state\":\"closed\"}", TestContext.Current.CancellationToken);
            }

            await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}", null, TestContext.Current.CancellationToken);
            directory.Delete(true);
        }
    }
}
