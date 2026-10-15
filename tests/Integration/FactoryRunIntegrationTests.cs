using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned clone answer in tests/Specs/Unit/FactoryRunHarness.cs is what git and GitHub give.</summary>
[Collection(GitHubWrites.Name)]
public sealed class FactoryRunIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    [Fact]
    public async Task ARunOnACloneWithoutTheLayoutEndsAtTheLayoutCheckWithOneComment()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN")
            ?? throw new InvalidOperationException("FACTORY_GITHUB_TOKEN is not set");
        using var http = new HttpClient();
        var client = new GitHubClient(http, token);
        string runId = Guid.NewGuid().ToString("N")[..8];
        DirectoryInfo parent = Directory.CreateTempSubdirectory("factory-run-integration-");
        int issue = 0;
        try
        {
            JsonElement created = await client.SendAsync(
                HttpMethod.Post,
                $"/repos/{Owner}/{Repo}/issues",
                JsonSerializer.Serialize(new { title = "FactoryRun integration " + runId, body = "Layout check." }),
                ct);
            issue = created.GetProperty("number").GetInt32();

            var request = new FactoryRunRequest(
                Owner, Repo, issue, token, runId, Path.Combine(parent.FullName, "clone"), "main",
                "factory-sandbox:integration", parent.FullName, "plan-model", 1, "build-model", 1, "review-model", 1,
                "Factory Integration Test", "factory-integration@example.com", []);
            FactoryRunResult result = await new FactoryRun(new CommandRunner(), client).RunAsync(request, ct);

            Assert.Null(result.PullRequest);
            Assert.Equal(0, result.Redos);
            JsonElement comments = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/issues/{issue}/comments", null, ct);
            string body = Assert.Single(comments.EnumerateArray().ToList()).GetProperty("body").GetString() ?? "";
            foreach (string path in new[] { "Solution.slnx", "specs/features/", "tests/Specs/", "tests/Integration/" })
            {
                Assert.Contains(path, body, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (issue is not 0)
            {
                await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/issues/{issue}", "{\"state\":\"closed\"}", TestContext.Current.CancellationToken);
            }

            parent.Delete(true);
        }
    }
}
