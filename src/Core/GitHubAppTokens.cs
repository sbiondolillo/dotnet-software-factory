using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core;

/// <summary>Mints and revokes installation tokens of a GitHub App, with a JWT signed by the App's private key.</summary>
public sealed class GitHubAppTokens(
    HttpMessageHandler handler,
    string appId,
    string installationId,
    string privateKeyPem,
    TimeProvider? time = null)
{
    private const string Root = "https://api.github.com";

    private const string JwtHeader = """{"alg":"RS256","typ":"JWT"}""";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Mints a token for <paramref name="repo"/> alone: contents write when <paramref name="write"/> is true, read otherwise.</summary>
    public async Task<string> MintAsync(string repo, bool write, CancellationToken cancellationToken = default)
    {
        string path = $"/app/installations/{installationId}/access_tokens";
        string body = JsonSerializer.Serialize(new
        {
            repositories = new[] { repo },
            permissions = new { contents = write ? "write" : "read", metadata = "read" },
        });
        using HttpRequestMessage request = Request(HttpMethod.Post, path, CreateJwt());
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using HttpClient http = Client();
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created)
        {
            throw new GitHubRequestException("POST", path, (int)response.StatusCode, text);
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("token").GetString()
            ?? throw new GitHubRequestException("POST", path, (int)response.StatusCode, "the answer holds no token");
    }

    /// <summary>Revokes <paramref name="token"/> with <c>DELETE /installation/token</c>.</summary>
    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        const string path = "/installation/token";
        using HttpRequestMessage request = Request(HttpMethod.Delete, path, token);
        using HttpClient http = Client();
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.NoContent)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GitHubRequestException("DELETE", path, (int)response.StatusCode, text);
        }
    }

    // The caller owns the handler, so the client leaves it open, and a client for each request costs no connection.
    private HttpClient Client() => new(handler, disposeHandler: false);

    private static HttpRequestMessage Request(HttpMethod method, string path, string bearer)
    {
        var request = new HttpRequestMessage(method, new Uri(Root + path, UriKind.Absolute));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("User-Agent", "dotnet-software-factory");
        return request;
    }

    /// <summary>
    /// A JWT that GitHub takes as the App. It is issued 60 seconds ago, against a clock that runs ahead of GitHub's. It expires
    /// 10 minutes after that, which is 9 minutes from now, because GitHub refuses an expiry more than 10 minutes ahead of its clock.
    /// </summary>
    private string CreateJwt()
    {
        long issued = _time.GetUtcNow().ToUnixTimeSeconds() - 60;
        string payload = JsonSerializer.Serialize(new { iat = issued, exp = issued + 600, iss = appId });
        string unsigned = $"{Encode(Encoding.UTF8.GetBytes(JwtHeader))}.{Encode(Encoding.UTF8.GetBytes(payload))}";
        using var key = RSA.Create();
        key.ImportFromPem(privateKeyPem);
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Encode(signature)}";
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
