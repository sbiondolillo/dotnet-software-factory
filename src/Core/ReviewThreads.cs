using System.Text.Json;

namespace Core;

/// <summary>A review of a pull request: the login of its author and its state.</summary>
public sealed record Review(string Author, string State);

/// <summary>A comment of a review thread.</summary>
public sealed record ReviewComment(string Author, string Body);

/// <summary>A review thread: id, resolved state, path, line (null once a commit changed it) and comments.</summary>
public sealed record ReviewThread(string Id, bool IsResolved, string Path, int? Line, IReadOnlyList<ReviewComment> Comments);

/// <summary>Reads, replies to and resolves review threads through GitHub's GraphQL API.</summary>
public sealed class ReviewThreads(GitHubClient client)
{
    private const string ReviewsQuery = """
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              reviews(first: 100) { nodes { state author { login } } }
            }
          }
        }
        """;

    private const string ThreadsQuery = """
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              reviewThreads(first: 100) {
                nodes {
                  id
                  isResolved
                  path
                  line
                  comments(first: 100) { nodes { author { login } body } }
                }
              }
            }
          }
        }
        """;

    private const string ReplyMutation = """
        mutation($threadId: ID!, $body: String!) {
          addPullRequestReviewThreadReply(input: {pullRequestReviewThreadId: $threadId, body: $body}) {
            comment { id }
          }
        }
        """;

    private const string ResolveMutation = """
        mutation($threadId: ID!) {
          resolveReviewThread(input: {threadId: $threadId}) {
            thread { isResolved }
          }
        }
        """;

    /// <summary>Reads every review of a pull request.</summary>
    public async Task<IReadOnlyList<Review>> ReadReviewsAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement answer = await SendAsync(ReviewsQuery, new { owner, name = repo, number }, cancellationToken).ConfigureAwait(false);
        var reviews = new List<Review>();
        foreach (JsonElement node in Nodes(answer, "reviews"))
        {
            reviews.Add(new Review(Login(node), Text(node, "state")));
        }

        return reviews;
    }

    /// <summary>Reads every review thread of a pull request.</summary>
    public async Task<IReadOnlyList<ReviewThread>> ReadThreadsAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement answer = await SendAsync(ThreadsQuery, new { owner, name = repo, number }, cancellationToken).ConfigureAwait(false);
        var threads = new List<ReviewThread>();
        foreach (JsonElement node in Nodes(answer, "reviewThreads"))
        {
            int? line = node.TryGetProperty("line", out JsonElement l) && l.ValueKind is JsonValueKind.Number ? l.GetInt32() : null;
            bool resolved = node.TryGetProperty("isResolved", out JsonElement r) && r.ValueKind is JsonValueKind.True;
            var comments = new List<ReviewComment>();
            if (node.TryGetProperty("comments", out JsonElement c) && c.ValueKind is JsonValueKind.Object
                && c.TryGetProperty("nodes", out JsonElement cn) && cn.ValueKind is JsonValueKind.Array)
            {
                foreach (JsonElement comment in cn.EnumerateArray())
                {
                    comments.Add(new ReviewComment(Login(comment), Text(comment, "body")));
                }
            }

            threads.Add(new ReviewThread(Text(node, "id"), resolved, Text(node, "path"), line, comments));
        }

        return threads;
    }

    /// <summary>Replies to a review thread.</summary>
    public async Task ReplyAsync(string threadId, string body, CancellationToken cancellationToken = default) =>
        await SendAsync(ReplyMutation, new { threadId, body }, cancellationToken).ConfigureAwait(false);

    /// <summary>Resolves a review thread.</summary>
    public async Task ResolveAsync(string threadId, CancellationToken cancellationToken = default) =>
        await SendAsync(ResolveMutation, new { threadId }, cancellationToken).ConfigureAwait(false);

    private static List<JsonElement> Nodes(JsonElement answer, string connection)
    {
        if (answer.ValueKind is JsonValueKind.Object
            && answer.TryGetProperty("data", out JsonElement data) && data.ValueKind is JsonValueKind.Object
            && data.TryGetProperty("repository", out JsonElement repository) && repository.ValueKind is JsonValueKind.Object
            && repository.TryGetProperty("pullRequest", out JsonElement pull) && pull.ValueKind is JsonValueKind.Object
            && pull.TryGetProperty(connection, out JsonElement conn) && conn.ValueKind is JsonValueKind.Object
            && conn.TryGetProperty("nodes", out JsonElement nodes) && nodes.ValueKind is JsonValueKind.Array)
        {
            return [.. nodes.EnumerateArray()];
        }

        return [];
    }

    private static string Login(JsonElement node) =>
        node.TryGetProperty("author", out JsonElement author) && author.ValueKind is JsonValueKind.Object
            ? Text(author, "login")
            : "";

    private static string Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String ? value.GetString() ?? "" : "";

    private async Task<JsonElement> SendAsync(string query, object variables, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new { query, variables });
        JsonElement answer = await client.SendAsync(HttpMethod.Post, "/graphql", json, cancellationToken).ConfigureAwait(false);
        if (answer.ValueKind is JsonValueKind.Object
            && answer.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind is JsonValueKind.Array)
        {
            var messages = new List<string>();
            foreach (JsonElement error in errors.EnumerateArray())
            {
                messages.Add(error.ValueKind is JsonValueKind.Object ? Text(error, "message") : error.ToString());
            }

            throw new GitHubGraphQLException(messages);
        }

        return answer;
    }
}
