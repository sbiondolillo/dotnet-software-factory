using System.Text.Json;

namespace Core;

/// <summary>A review of a pull request.</summary>
public sealed record Review(string Author, string State);

/// <summary>A comment in a review thread.</summary>
public sealed record ReviewThreadComment(string Author, string Body);

/// <summary>A review thread of a pull request.</summary>
public sealed record ReviewThread(string Id, bool IsResolved, string Path, int? Line, IReadOnlyList<ReviewThreadComment> Comments);

/// <summary>Reads, replies to and resolves review threads through GitHub's GraphQL API.</summary>
public sealed class ReviewThreads(GitHubClient client)
{
    private const string ReviewsQuery = "query($owner: String!, $name: String!, $number: Int!) { repository(owner: $owner, name: $name) { pullRequest(number: $number) { reviews(first: 100) { nodes { state author { login } } } } } }";
    private const string ThreadsQuery = "query($owner: String!, $name: String!, $number: Int!) { repository(owner: $owner, name: $name) { pullRequest(number: $number) { reviewThreads(first: 100) { nodes { id isResolved path line comments(first: 100) { nodes { author { login } body } } } } } } }";
    private const string ReplyQuery = "mutation($threadId: ID!, $body: String!) { addPullRequestReviewThreadReply(input: {pullRequestReviewThreadId: $threadId, body: $body}) { comment { id } } }";
    private const string ResolveQuery = "mutation($threadId: ID!) { resolveReviewThread(input: {threadId: $threadId}) { thread { isResolved } } }";

    /// <summary>Reads every review of a pull request.</summary>
    public async Task<IReadOnlyList<Review>> ReadReviewsAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendAsync(ReviewsQuery, new { owner, name = repo, number }, cancellationToken).ConfigureAwait(false);
        var reviews = new List<Review>();
        foreach (JsonElement node in Nodes(data.GetProperty("repository").GetProperty("pullRequest").GetProperty("reviews")))
        {
            reviews.Add(new Review(Login(node), node.GetProperty("state").GetString() ?? ""));
        }

        return reviews;
    }

    /// <summary>Reads every review thread of a pull request.</summary>
    public async Task<IReadOnlyList<ReviewThread>> ReadThreadsAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendAsync(ThreadsQuery, new { owner, name = repo, number }, cancellationToken).ConfigureAwait(false);
        var threads = new List<ReviewThread>();
        foreach (JsonElement node in Nodes(data.GetProperty("repository").GetProperty("pullRequest").GetProperty("reviewThreads")))
        {
            var comments = new List<ReviewThreadComment>();
            foreach (JsonElement comment in Nodes(node.GetProperty("comments")))
            {
                comments.Add(new ReviewThreadComment(Login(comment), comment.GetProperty("body").GetString() ?? ""));
            }

            int? line = node.TryGetProperty("line", out JsonElement l) && l.ValueKind is JsonValueKind.Number ? l.GetInt32() : null;
            threads.Add(new ReviewThread(
                node.GetProperty("id").GetString() ?? "",
                node.GetProperty("isResolved").GetBoolean(),
                node.GetProperty("path").GetString() ?? "",
                line,
                comments));
        }

        return threads;
    }

    /// <summary>Adds a reply to a review thread.</summary>
    public async Task ReplyAsync(string threadId, string body, CancellationToken cancellationToken = default) =>
        await SendAsync(ReplyQuery, new { threadId, body }, cancellationToken).ConfigureAwait(false);

    /// <summary>Marks a review thread as resolved.</summary>
    public async Task ResolveAsync(string threadId, CancellationToken cancellationToken = default) =>
        await SendAsync(ResolveQuery, new { threadId }, cancellationToken).ConfigureAwait(false);

    private static JsonElement.ArrayEnumerator Nodes(JsonElement connection) => connection.GetProperty("nodes").EnumerateArray();

    private static string Login(JsonElement node) =>
        node.TryGetProperty("author", out JsonElement author) && author.ValueKind is JsonValueKind.Object
            ? author.GetProperty("login").GetString() ?? ""
            : "";

    private async Task<JsonElement> SendAsync(string query, object variables, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new { query, variables });
        JsonElement answer = await client.SendAsync(HttpMethod.Post, "/graphql", json, cancellationToken).ConfigureAwait(false);
        if (answer.ValueKind is JsonValueKind.Object && answer.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind is JsonValueKind.Array && errors.GetArrayLength() is not 0)
        {
            var messages = new List<string>();
            foreach (JsonElement error in errors.EnumerateArray())
            {
                messages.Add(error.ValueKind is JsonValueKind.Object && error.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "");
            }

            throw new GitHubGraphQLException(messages);
        }

        return answer.GetProperty("data");
    }
}
