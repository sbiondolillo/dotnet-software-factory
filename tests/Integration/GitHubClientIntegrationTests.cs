using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned GitHub answers in tests/Specs are what the real API gives.</summary>
[Collection(GitHubWrites.Name)]
public sealed class GitHubClientIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";
    private static readonly string[] BugAndQuestion = ["bug", "question"];
    private static readonly string[] CleanupAndReframe = ["cleanup", "reframe"];

    private static GitHubClient Client(string? token = null)
    {
        token ??= Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    private static async Task<(int Number, string Login)> CreateAsync(GitHubClient client, Cleanup cleanup, string json, CancellationToken ct)
    {
        JsonElement created = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/issues", json, ct);
        int number = created.GetProperty("number").GetInt32();
        cleanup.Add($"close the issue #{number}", () => client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/issues/{number}", "{\"state\":\"closed\"}"));
        return (number, created.GetProperty("user").GetProperty("login").GetString() ?? "");
    }

    [Fact]
    public async Task AnIssueReadsBackWithTitleBodyAuthorAndLabelsAndTakesAComment()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string title = "client-test " + Guid.NewGuid();
        string comment = "comment " + Guid.NewGuid();
        await Cleanup.RunAsync(async cleanup =>
        {
            (int number, string login) = await CreateAsync(client, cleanup, JsonSerializer.Serialize(new { title, body = "the body", labels = BugAndQuestion }), ct);
            GitHubIssue issue = await client.ReadIssueAsync(Owner, Repo, number, ct);
            Assert.Equal(title, issue.Title);
            Assert.Equal("the body", issue.Body);
            Assert.Equal(login, issue.Author);
            Assert.Equal<string[]>(["bug", "question"], [.. issue.Labels.Order()]);

            await client.PostCommentAsync(Owner, Repo, number, comment, ct);
            JsonElement comments = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/issues/{number}/comments", null, ct);
            Assert.Equal(comment, Assert.Single(comments.EnumerateArray()).GetProperty("body").GetString());
        });
    }

    [Fact]
    public async Task AnIssueWithTwoExcludingLabelsAndAMultiLineBodyReadsBackWithBoth()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string written = "## Scope\nREADME.md\n";
        await Cleanup.RunAsync(async cleanup =>
        {
            (int number, _) = await CreateAsync(client, cleanup, JsonSerializer.Serialize(new { title = "client-test " + Guid.NewGuid(), body = written, labels = CleanupAndReframe }), ct);
            GitHubIssue issue = await client.ReadIssueAsync(Owner, Repo, number, ct);
            Assert.Equal<string[]>(["cleanup", "reframe"], [.. issue.Labels.Order()]);
            Assert.Equal(written, issue.Body);
            Assert.Equal<string[]>(["README.md"], [.. CleanupBody.ReadScope(issue.Body)]);
            Assert.Null(CleanupBody.ReadCheck(issue.Body));
        });
    }

    [Fact]
    public async Task AnIssueWithNoLabelAndAScenariosHeadingWithNoFenceReadsBackAsItWasWritten()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        string written = "## Scenarios\n\nThe scenarios follow.";
        await Cleanup.RunAsync(async cleanup =>
        {
            (int number, _) = await CreateAsync(client, cleanup, JsonSerializer.Serialize(new { title = "client-test " + Guid.NewGuid(), body = written }), ct);
            GitHubIssue issue = await client.ReadIssueAsync(Owner, Repo, number, ct);
            Assert.Empty(issue.Labels);
            Assert.Equal(written, issue.Body);
            Assert.True(IssueScenarios.Holds(issue.Body));
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-client-integration-");
            cleanup.Add($"delete the directory {directory.FullName}", () => directory.Delete(recursive: true));
            IssueScenariosResult result = IssueScenarios.Apply(issue.Body, directory.FullName);
            Assert.Contains("holds no fenced block", Assert.Single(result.Faults), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AnIssueWithATitleAloneReadsWithEmptyBodyAndNoLabels()
    {
        GitHubIssue issue = await Client().ReadIssueAsync(Owner, Repo, ScratchFixtures.TitleAloneIssue, TestContext.Current.CancellationToken);
        Assert.Equal("", issue.Body);
        Assert.Empty(issue.Labels);
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
        await Cleanup.RunAsync(async cleanup =>
        {
            (int number, _) = await CreateAsync(client, cleanup, JsonSerializer.Serialize(new { title = "client-test " + Guid.NewGuid() }), ct);
            await client.PostCommentAsync(Owner, Repo, number, new string('a', 262144), ct);
            GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => client.PostCommentAsync(Owner, Repo, number, new string('é', 131073), ct));
            Assert.Equal(422, ex.StatusCode);
        });
    }

    [Fact]
    public async Task AnInvalidTokenGives401()
    {
        GitHubRequestException ex = await Assert.ThrowsAsync<GitHubRequestException>(() => Client("invalid").ReadIssueAsync(Owner, Repo, 1, TestContext.Current.CancellationToken));
        Assert.Equal(401, ex.StatusCode);
    }
}
