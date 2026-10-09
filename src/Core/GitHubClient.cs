using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Polly;
using Polly.Retry;

namespace Core;

/// <summary>Sends requests to GitHub's REST API through an <see cref="HttpClient"/>.</summary>
public sealed class GitHubClient(HttpClient httpClient, string token, TimeSpan? baseWait = null, TimeSpan? timeout = null)
{
    private const string Root = "https://api.github.com";

    private readonly ResiliencePipeline<Answer> _pipeline = BuildPipeline(baseWait ?? TimeSpan.FromSeconds(2));

    /// <summary>The wait that the client's own retry builds on, 2 seconds when none was given.</summary>
    public TimeSpan BaseWait { get; } = baseWait ?? TimeSpan.FromSeconds(2);

    /// <summary>The time one request may take before it counts as failed, 30 seconds when none was given.</summary>
    public TimeSpan Timeout { get; } = timeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Sends a request and gives back the JSON of the answer. A transient failure, or a request that times out, is sent again, up to 3 more times. When every attempt times out it throws <see cref="TimeoutException"/>.</summary>
    public async Task<JsonElement> SendAsync(HttpMethod method, string path, string? jsonBody = null, CancellationToken cancellationToken = default)
    {
        Answer answer = await _pipeline.ExecuteAsync(
            async token => await SendOnceAsync(method, path, jsonBody, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        if (answer.TimedOut)
        {
            throw new TimeoutException($"{method.Method} {path} got no answer within {Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, on each of 4 attempts.");
        }

        if (answer.Status is < 200 or > 299)
        {
            throw new GitHubRequestException(method.Method, path, answer.Status, answer.Text);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(answer.Text) ? "null" : answer.Text);
        return document.RootElement.Clone();
    }

    private static bool IsTransient(Answer answer) =>
        answer.TimedOut || answer.Status >= 500 || answer.Status is 429 or 409 || (answer.Status is 403 && answer.RetryAfter is not null);

    private static ResiliencePipeline<Answer> BuildPipeline(TimeSpan baseWait) =>
        new ResiliencePipelineBuilder<Answer>()
            .AddRetry(new RetryStrategyOptions<Answer>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = baseWait,
                ShouldHandle = args => new ValueTask<bool>(args.Outcome.Result is { } answer && IsTransient(answer)),
                DelayGenerator = args => new ValueTask<TimeSpan?>(args.Outcome.Result?.RetryAfter),
            })
            .Build();

    private async Task<Answer> SendOnceAsync(HttpMethod method, string path, string? jsonBody, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);
        try
        {
            return await SendRequestAsync(method, path, jsonBody, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && (timeoutSource.IsCancellationRequested || ex.InnerException is TimeoutException))
        {
            return new Answer(0, "", null, true);
        }
    }

    private async Task<Answer> SendRequestAsync(HttpMethod method, string path, string? jsonBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Root + path, UriKind.Absolute));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("User-Agent", "dotnet-software-factory");
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        TimeSpan? retryAfter = header?.Delta ?? (header?.Date is { } date ? TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks)) : null);
        return new Answer((int)response.StatusCode, text, retryAfter, false);
    }

    private sealed record Answer(int Status, string Text, TimeSpan? RetryAfter, bool TimedOut);

    /// <summary>Reads an issue: title, body, author and labels.</summary>
    public async Task<GitHubIssue> ReadIssueAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        JsonElement json = await SendAsync(HttpMethod.Get, IssuePath(owner, repo, number), null, cancellationToken).ConfigureAwait(false);
        string title = json.GetProperty("title").GetString() ?? "";
        string body = json.TryGetProperty("body", out JsonElement b) && b.ValueKind is JsonValueKind.String ? b.GetString() ?? "" : "";
        string author = json.GetProperty("user").GetProperty("login").GetString() ?? "";
        var labels = new List<string>();
        if (json.TryGetProperty("labels", out JsonElement l) && l.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement label in l.EnumerateArray())
            {
                labels.Add(label.GetProperty("name").GetString() ?? "");
            }
        }

        return new GitHubIssue(title, body, author, labels);
    }

    /// <summary>Posts a comment on an issue.</summary>
    public async Task PostCommentAsync(string owner, string repo, int number, string text, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, string> { ["body"] = text });
        await SendAsync(HttpMethod.Post, IssuePath(owner, repo, number) + "/comments", json, cancellationToken).ConfigureAwait(false);
    }

    private static string IssuePath(string owner, string repo, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"/repos/{owner}/{repo}/issues/{number}");
}
