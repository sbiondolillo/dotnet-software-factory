using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned git, pull request and comment answers in tests/Specs/Steps/DeliverSteps.cs are what git and GitHub give.</summary>
[Collection(GitHubWrites.Name)]
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
        await Cleanup.RunAsync(async cleanup =>
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-deliver-integration-");
            cleanup.Add($"delete the directory {directory.FullName}", () => directory.Delete(true));
            JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/issues", JsonSerializer.Serialize(new { title }), ct);
            int issue = created.GetProperty("number").GetInt32();
            cleanup.Add($"close the issue #{issue}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/issues/{issue}", "{\"state\":\"closed\"}"));

            var git = new Git(new CommandRunner());
            await git.CloneAsync($"https://github.com/{Owner}/{Repo}.git", token, directory.FullName, ct);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, $"deliver-{runId}.txt"), runId, ct);
            await git.CommitAllAsync(directory.FullName, "Deliver integration " + runId, "Factory Integration Test", "factory-integration@example.com", ct);

            // The push in RunAsync creates the branch, and a failure after it leaves no answer to
            // record it by. So this step reads whether GitHub holds the branch.
            cleanup.Add($"delete the branch {branch} when GitHub holds it", () => DeleteWhenHeldAsync(client, branch));
            var pullRequests = new PullRequests(client);
            var deliver = new Deliver(git, pullRequests, client);
            PullRequest result = await deliver.RunAsync(directory.FullName, runId, Owner, Repo, "main", issue, title, token, ct);
            int pull = result.Number;
            cleanup.Add($"close the pull request #{pull}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{pull}", "{\"state\":\"closed\"}"));

            JsonElement pr = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/pulls/{pull}", null, ct);
            Assert.Equal(title, pr.GetProperty("title").GetString());
            Assert.StartsWith($"Closes #{issue}", pr.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal(result.HtmlUrl, pr.GetProperty("html_url").GetString());

            JsonElement comments = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/issues/{issue}/comments", null, ct);
            JsonElement only = Assert.Single(comments.EnumerateArray().ToList());
            Assert.Contains(result.HtmlUrl, only.GetProperty("body").GetString(), StringComparison.Ordinal);
        });
    }

    private static async Task DeleteWhenHeldAsync(GitHubClient client, string branch)
    {
        JsonElement refs = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/matching-refs/heads/{branch}", null);
        if (refs.EnumerateArray().Any(r => r.GetProperty("ref").GetString() == "refs/heads/" + branch))
        {
            await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}");
        }
    }
}
