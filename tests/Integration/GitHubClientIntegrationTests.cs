using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned GitHub answers in tests/Specs are what the real API gives.</summary>
public sealed class GitHubClientIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";
    private static readonly string[] BugAndQuestion = ["bug", "question"];

    private static GitHubClient Client(string? token = null)
    {
        token ??= Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    private static async Task<(int Number, string Login)> CreateAsync(GitHubClient client, string json, CancellationToken ct)
    {
        JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/issues", json, ct);
        return (created.GetProperty("number").GetInt32(), created.GetProperty("user").GetProperty("login").GetString() ?? "");
    }

    private static Task<JsonElement> CloseAsync(GitHubClient client, int number) =>
        client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/issues/{number}", "{\"state\":\"closed\"}");

    [Fact]
    public async Task AnIssueReadsBackWithTitleBodyAuthorAndLabelsAndTakesAComment()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string title = "client-test " + Guid.NewGuid();
        string comment = "comment " + Guid.NewGuid();
        (int number, string login) = await CreateAsync(client, JsonSerializer.Serialize(new { title, body = "the body", labels = BugAndQuestion }), ct);
        try
        {
            GitHubIssue issue = await client.ReadIssueAsync(Owner, Repo, number, ct);
            Assert.Equal(title, issue.Title);
            Assert.Equal("the body", issue.Body);
            Assert.Equal(login, issue.Author);
            Assert.Equal<string[]>(["bug", "question"], [.. issue.Labels.Order()]);

            await client.PostCommentAsync(Owner, Repo, number, comment, ct);
            JsonElement comments = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/issues/{number}/comments", null, ct);
            Assert.Equal(comment, Assert.Single(comments.EnumerateArray()).GetProperty("body").GetString());
        }
        finally
        {
            await CloseAsync(client, number);
        }
    }

    [Fact]
    public async Task AnIssueWithATitleAloneReadsWithEmptyBodyAndNoLabels()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        (int number, _) = await CreateAsync(client, JsonSerializer.Serialize(new { title = "client-test " + Guid.NewGuid() }), ct);
        try
        {
            GitHubIssue issue = await client.ReadIssueAsync(Owner, Repo, number, ct);
            Assert.Equal("", issue.Body);
            Assert.Empty(issue.Labels);
        }
        finally
        {
            await CloseAsync(client, number);
        }
    }

    [Fact]
    public async Task ReadingIssueZeroGives404()
    {
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Client().ReadIssueAsync(Owner, Repo, 0, TestContext.Current.CancellationToken));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ACommentBodyOverTheByteLimitThrowsWith422()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        (int number, _) = await CreateAsync(client, JsonSerializer.Serialize(new { title = "client-test " + Guid.NewGuid() }), ct);
        try
        {
            await client.PostCommentAsync(Owner, Repo, number, new string('a', 262144), ct);
            GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => client.PostCommentAsync(Owner, Repo, number, new string('é', 131073), ct));
            Assert.Equal(422, ex.StatusCode);
        }
        finally
        {
            await CloseAsync(client, number);
        }
    }

    [Fact]
    public async Task AnInvalidTokenGives401()
    {
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Client("invalid").ReadIssueAsync(Owner, Repo, 1, TestContext.Current.CancellationToken));
        Assert.Equal(401, ex.StatusCode);
    }
}
