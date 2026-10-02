using System.Text;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned review thread answers in tests/Specs are what GitHub gives.</summary>
public sealed class ReviewThreadsIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    private static GitHubClient Client(string? tokenOverride = null)
    {
        string? token = tokenOverride ?? Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    private sealed record Scratch(string Branch, int Number, string Path, string FileSha, string CommitSha);

    private static async Task<Scratch> CreatePullAsync(GitHubClient client, CancellationToken ct)
    {
        string branch = "rt-test/" + Guid.NewGuid().ToString("N");
        string path = branch.Replace('/', '-') + ".txt";
        JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
        string sha = main.GetProperty("object").GetProperty("sha").GetString() ?? "";
        await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha }), ct);
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes("one\ntwo\nthree\n"));
        JsonElement put = await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{path}", JsonSerializer.Serialize(new { message = "add " + path, content, branch }), ct);
        JsonElement pull = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/pulls", JsonSerializer.Serialize(new { title = "test " + branch, head = branch, @base = "main", body = "b" }), ct);
        return new Scratch(
            branch,
            pull.GetProperty("number").GetInt32(),
            path,
            put.GetProperty("content").GetProperty("sha").GetString() ?? "",
            put.GetProperty("commit").GetProperty("sha").GetString() ?? "");
    }

    private static async Task<JsonElement> CommentAsync(GitHubClient client, Scratch s, int line, CancellationToken ct) =>
        await client.SendAsync(
            HttpMethod.Post,
            $"/repos/{Owner}/{Repo}/pulls/{s.Number}/comments",
            JsonSerializer.Serialize(new { body = "comment on " + line, commit_id = s.CommitSha, path = s.Path, line, side = "RIGHT" }),
            ct);

    private static async Task CleanUpAsync(GitHubClient client, Scratch? s)
    {
        if (s is null)
        {
            return;
        }

        await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{s.Number}", "{\"state\":\"closed\"}");
        await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{s.Branch}");
    }

    [Fact]
    public async Task ANewPullRequestReadsEmptyReviewsAndThreads()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        Scratch? s = null;
        try
        {
            s = await CreatePullAsync(client, ct);
            var sut = new ReviewThreads(client);
            Assert.Empty(await sut.ReadReviewsAsync(Owner, Repo, s.Number, ct));
            Assert.Empty(await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct));
        }
        finally
        {
            await CleanUpAsync(client, s);
        }
    }

    [Fact]
    public async Task ACommentOpensAThreadAndAReviewWithTheAuthorAndState()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        Scratch? s = null;
        try
        {
            s = await CreatePullAsync(client, ct);
            JsonElement comment = await CommentAsync(client, s, 2, ct);
            string login = comment.GetProperty("user").GetProperty("login").GetString() ?? "";
            var sut = new ReviewThreads(client);
            ReviewThread thread = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct));
            Assert.False(thread.IsResolved);
            Assert.Equal(s.Path, thread.Path);
            Assert.Equal(2, thread.Line);
            Assert.Equal(new ReviewThreadComment(login, "comment on 2"), Assert.Single(thread.Comments));
            Assert.Equal(new Review(login, "COMMENTED"), Assert.Single(await sut.ReadReviewsAsync(Owner, Repo, s.Number, ct)));
        }
        finally
        {
            await CleanUpAsync(client, s);
        }
    }

    [Fact]
    public async Task AReplyKeepsItsBodyAndResolveLeavesOtherThreadsAlone()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        Scratch? s = null;
        try
        {
            s = await CreatePullAsync(client, ct);
            await CommentAsync(client, s, 1, ct);
            await CommentAsync(client, s, 3, ct);
            var sut = new ReviewThreads(client);
            IReadOnlyList<ReviewThread> threads = await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct);
            Assert.Equal(2, threads.Count);
            ReviewThread first = threads.Single(t => t.Line is 1);
            ReviewThread other = threads.Single(t => t.Line is 3);

            const string body = "say \"hi\"\nagain";
            await sut.ReplyAsync(first.Id, body, ct);
            await sut.ResolveAsync(first.Id, ct);
            await sut.ResolveAsync(first.Id, ct);

            threads = await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct);
            ReviewThread after = threads.Single(t => t.Id == first.Id);
            Assert.True(after.IsResolved);
            Assert.Equal(body, after.Comments[^1].Body);
            Assert.False(threads.Single(t => t.Id == other.Id).IsResolved);
        }
        finally
        {
            await CleanUpAsync(client, s);
        }
    }

    [Fact]
    public async Task AnEmptyReplyGivesNoErrorAndAddsNoComment()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        Scratch? s = null;
        try
        {
            s = await CreatePullAsync(client, ct);
            await CommentAsync(client, s, 2, ct);
            var sut = new ReviewThreads(client);
            ReviewThread thread = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct));
            await sut.ReplyAsync(thread.Id, "", ct);
            await sut.ReplyAsync(thread.Id, "  ", ct);
            ReviewThread after = (await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct)).Single(t => t.Id == thread.Id);
            Assert.Single(after.Comments);
        }
        finally
        {
            await CleanUpAsync(client, s);
        }
    }

    [Fact]
    public async Task AChangedLineReadsAsNull()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        Scratch? s = null;
        try
        {
            s = await CreatePullAsync(client, ct);
            await CommentAsync(client, s, 3, ct);
            string content = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed\n"));
            await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{s.Path}", JsonSerializer.Serialize(new { message = "shrink", content, sha = s.FileSha, branch = s.Branch }), ct);
            var sut = new ReviewThreads(client);
            ReviewThread thread = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct));
            for (int i = 0; i < 30 && thread.Line is not null; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                thread = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, s.Number, ct));
            }

            Assert.Null(thread.Line);
        }
        finally
        {
            await CleanUpAsync(client, s);
        }
    }

    [Fact]
    public async Task UnknownIdsAndPullRequestsThrowGraphQlExceptions()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var sut = new ReviewThreads(Client());
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReplyAsync("PRRT_unknown", "x", ct));
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ResolveAsync("PRRT_unknown", ct));
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadReviewsAsync(Owner, Repo, 0, ct));
        await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadThreadsAsync(Owner, Repo, 0, ct));
    }

    [Fact]
    public async Task AnInvalidTokenThrowsRequestExceptionWith401()
    {
        var sut = new ReviewThreads(Client("invalid"));
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadThreadsAsync(Owner, Repo, 1, TestContext.Current.CancellationToken));
        Assert.Equal(401, ex.StatusCode);
    }
}
