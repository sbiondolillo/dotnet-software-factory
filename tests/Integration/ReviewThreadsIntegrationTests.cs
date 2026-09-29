using System.Text;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the canned GraphQL answers in tests/Specs are what GitHub gives.</summary>
public sealed class ReviewThreadsIntegrationTests
{
    private const string Owner = "sbiondolillo";
    private const string Repo = "factory-scratch";

    private static GitHubClient Client()
    {
        string? token = Environment.GetEnvironmentVariable("FACTORY_GITHUB_TOKEN");
        Assert.False(string.IsNullOrEmpty(token), "FACTORY_GITHUB_TOKEN is not set");
        return new GitHubClient(new HttpClient(), token);
    }

    [Fact]
    public async Task ThreadsAreOpenedRepliedResolvedAndTheirLineGoesNull()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        GitHubClient client = Client();
        var sut = new ReviewThreads(client);
        string branch = "rt-test/" + Guid.NewGuid().ToString("N");
        int pull = 0;
        try
        {
            JsonElement main = await client.SendAsync(HttpMethod.Get, $"/repos/{Owner}/{Repo}/git/ref/heads/main", null, ct);
            string baseSha = main.GetProperty("object").GetProperty("sha").GetString() ?? "";
            await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/git/refs", JsonSerializer.Serialize(new { @ref = "refs/heads/" + branch, sha = baseSha }), ct);
            string file = branch.Replace('/', '-') + ".txt";
            JsonElement created = await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{file}", JsonSerializer.Serialize(new { message = "add", content = Convert.ToBase64String(Encoding.UTF8.GetBytes("a\nb\nc\n")), branch }), ct);
            string fileSha = created.GetProperty("content").GetProperty("sha").GetString() ?? "";
            string commitSha = created.GetProperty("commit").GetProperty("sha").GetString() ?? "";
            JsonElement pr = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/pulls", JsonSerializer.Serialize(new { head = branch, @base = "main", title = "rt " + branch, body = "b" }), ct);
            pull = pr.GetProperty("number").GetInt32();

            Assert.Empty(await sut.ReadReviewsAsync(Owner, Repo, pull, ct));
            Assert.Empty(await sut.ReadThreadsAsync(Owner, Repo, pull, ct));

            JsonElement comment = await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/pulls/{pull}/comments", JsonSerializer.Serialize(new { body = "first", commit_id = commitSha, path = file, line = 2, side = "RIGHT" }), ct);
            string login = comment.GetProperty("user").GetProperty("login").GetString() ?? "";
            Review review = Assert.Single(await sut.ReadReviewsAsync(Owner, Repo, pull, ct));
            Assert.Equal(new Review(login, "COMMENTED"), review);
            ReviewThread thread = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, pull, ct));
            Assert.False(thread.IsResolved);
            Assert.Equal(2, thread.Line);
            Assert.Equal(new ReviewComment(login, "first"), Assert.Single(thread.Comments));

            await client.SendAsync(HttpMethod.Post, $"/repos/{Owner}/{Repo}/pulls/{pull}/comments", JsonSerializer.Serialize(new { body = "second", commit_id = commitSha, path = file, line = 3, side = "RIGHT" }), ct);
            IReadOnlyList<ReviewThread> both = await sut.ReadThreadsAsync(Owner, Repo, pull, ct);
            Assert.Equal(2, both.Count);
            ReviewThread other = Assert.Single(both, t => t.Id != thread.Id);
            Assert.False(other.IsResolved);
            Assert.Equal(new ReviewComment(login, "second"), Assert.Single(other.Comments));

            const string Reply = "say \"hi\"\nthere";
            await sut.ReplyAsync(thread.Id, Reply, ct);
            thread = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, pull, ct), t => t.Id == thread.Id);
            Assert.Equal(Reply, thread.Comments[^1].Body);

            await sut.ResolveAsync(thread.Id, ct);
            await sut.ResolveAsync(thread.Id, ct);
            IReadOnlyList<ReviewThread> after = await sut.ReadThreadsAsync(Owner, Repo, pull, ct);
            Assert.True(Assert.Single(after, t => t.Id == thread.Id).IsResolved);
            ReviewThread otherAfter = Assert.Single(after, t => t.Id == other.Id);
            Assert.False(otherAfter.IsResolved);
            Assert.Equal(new ReviewComment(login, "second"), Assert.Single(otherAfter.Comments));

            await client.SendAsync(HttpMethod.Put, $"/repos/{Owner}/{Repo}/contents/{file}", JsonSerializer.Serialize(new { message = "change", content = Convert.ToBase64String(Encoding.UTF8.GetBytes("x\ny\nz\n")), branch, sha = fileSha }), ct);
            int? line = 2;
            string firstId = thread.Id;
            for (int i = 0; i < 40 && line is not null; i++)
            {
                await Task.Delay(500, ct);
                line = Assert.Single(await sut.ReadThreadsAsync(Owner, Repo, pull, ct), t => t.Id == firstId).Line;
            }

            Assert.Null(line);

            await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReplyAsync("PRRT_unknown", "x", ct));
            await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ResolveAsync("PRRT_unknown", ct));
            await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadReviewsAsync(Owner, Repo, 0, ct));
            await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadThreadsAsync(Owner, Repo, 0, ct));
        }
        finally
        {
            if (pull is not 0)
            {
                await client.SendAsync(HttpMethod.Patch, $"/repos/{Owner}/{Repo}/pulls/{pull}", "{\"state\":\"closed\"}", TestContext.Current.CancellationToken);
            }

            await client.SendAsync(HttpMethod.Delete, $"/repos/{Owner}/{Repo}/git/refs/heads/{branch}", null, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AnInvalidTokenThrowsRequestExceptionWith401()
    {
        var sut = new ReviewThreads(new GitHubClient(new HttpClient(), "invalid"));
        var ex = await Assert.ThrowsAsync<GitHubRequestException>(() => sut.ReadReviewsAsync(Owner, Repo, 1, TestContext.Current.CancellationToken));
        Assert.Equal(401, ex.StatusCode);
    }
}
