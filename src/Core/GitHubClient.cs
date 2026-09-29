using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Core;

/// <summary>Sends requests to GitHub's REST API through an <see cref="HttpClient"/>.</summary>
public sealed class GitHubClient(HttpClient httpClient, string token)
{
    private const string Root = "https://api.github.com";

    /// <summary>Sends a request and gives back the JSON of the answer.</summary>
    public async Task<JsonElement> SendAsync(HttpMethod method, string path, string? jsonBody = null, CancellationToken cancellationToken = default)
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
        int status = (int)response.StatusCode;
        if (status is < 200 or > 299)
        {
            throw new GitHubRequestException(method.Method, path, status, text);
        }

        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
        return document.RootElement.Clone();
    }

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
