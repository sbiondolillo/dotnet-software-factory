using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core;

/// <summary>Sends every request of the factory to the Managed Agents API: sessions, events, resources and files.</summary>
public sealed class ManagedAgentsClient(
    HttpClient http,
    string apiKey,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>The beta header every request carries.</summary>
    public const string Beta = "managed-agents-2026-04-01";

    private const string FilesBeta = "files-api-2025-04-14";
    private const string Root = "https://api.anthropic.com";
    private const int Attempts = 4;

    /// <summary>The id of the agent each session runs. A session cannot start while it is empty.</summary>
    public string AgentId { get; init; } = "";

    /// <summary>The id of the environment each session runs in. A session cannot start while it is empty.</summary>
    public string EnvironmentId { get; init; } = "";

    internal HttpClient Http { get; } = http;

    internal string ApiKey { get; } = apiKey;

    internal Func<TimeSpan, CancellationToken, Task> Delay { get; } = delay ?? ((wait, token) => Task.Delay(wait, token));

    /// <summary>The wait between two reads of a list that the agent polls, and the base of the wait before a request is sent again.</summary>
    internal static TimeSpan PollDelay { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Sends a JSON request and gives back the JSON of the answer. A GET that gets 429, a status of 500 or more, or no
    /// answer is sent again, up to 3 more times. A POST that gets 429 or 529 is sent again in the same way. Any other status outside
    /// 200 to 299 throws <see cref="ClaudeCodeException"/>.</summary>
    internal async Task<JsonElement> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        string text = await SendCoreAsync(
            method,
            path,
            () => body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            (content, token) => content.ReadAsStringAsync(token),
            cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return document.RootElement.Clone();
    }

    /// <summary>Uploads the file at <paramref name="localPath"/> with <c>POST /v1/files</c> and gives back its file id.</summary>
    internal async Task<string> UploadAsync(string localPath, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(localPath, cancellationToken).ConfigureAwait(false);
        string name = Path.GetFileName(localPath);
        string text = await SendCoreAsync(
            HttpMethod.Post,
            "/v1/files",
            () =>
            {
                var content = new MultipartFormDataContent();
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(file, "file", name);
                return content;
            },
            (content, token) => content.ReadAsStringAsync(token),
            cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("id", out JsonElement id) && id.ValueKind is JsonValueKind.String && id.GetString() is { Length: > 0 } value
            ? value
            : throw new ClaudeCodeException($"POST /v1/files for {name} answered with no file id.");
    }

    /// <summary>Downloads the bytes of the file <paramref name="fileId"/>.</summary>
    internal Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken) =>
        SendCoreAsync(
            HttpMethod.Get,
            $"/v1/files/{Uri.EscapeDataString(fileId)}/content",
            () => null,
            (content, token) => content.ReadAsByteArrayAsync(token),
            cancellationToken);

    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>A 429 or a 529 says that the API did not process the request, so a POST with that answer is safe to send again.</summary>
    private static bool IsUnprocessed(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests || (int)status is 529;

    private static ClaudeCodeException Failure(HttpMethod method, string path, int status, string text) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"{method.Method} {path} answered with the status code {status}: {(text.Length > 500 ? text[..500] : text)}"));

    private async Task<T> SendCoreAsync<T>(
        HttpMethod method,
        string path,
        Func<HttpContent?> content,
        Func<HttpContent, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        // A GET is sent again on each transient failure. A POST is sent again only on an answer that says the API did not
        // process it, so a retry never repeats a side effect.
        bool retries = method == HttpMethod.Get;
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = Request(method, path, content());
            try
            {
                using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await read(response.Content, cancellationToken).ConfigureAwait(false);
                }

                string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (attempt >= Attempts || !(retries ? IsTransient(response.StatusCode) : IsUnprocessed(response.StatusCode)))
                {
                    throw Failure(method, path, (int)response.StatusCode, text);
                }
            }
            catch (HttpRequestException) when (retries && attempt < Attempts)
            {
            }
            catch (TaskCanceledException) when (retries && attempt < Attempts && !cancellationToken.IsCancellationRequested)
            {
            }

            await Delay(PollDelay * attempt, cancellationToken).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content)
    {
        Uri uri = Http.BaseAddress is null ? new Uri(Root + path, UriKind.Absolute) : new Uri(Http.BaseAddress, path);
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Headers.TryAddWithoutValidation(
            "anthropic-beta",
            path.StartsWith("/v1/files", StringComparison.Ordinal) ? $"{Beta},{FilesBeta}" : Beta);
        return request;
    }
}
