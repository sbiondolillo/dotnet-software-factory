using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core;

/// <summary>
/// Mints and revokes installation tokens of a GitHub App, with a JWT signed by the App's private key. Each request goes through a
/// <see cref="GitHubClient"/>, so a transient failure is sent again.
/// </summary>
public sealed class GitHubAppTokens(
    HttpMessageHandler handler,
    string appId,
    string installationId,
    string privateKeyPem,
    TimeProvider? time = null,
    TimeSpan? baseWait = null)
{
    private const string JwtHeader = """{"alg":"RS256","typ":"JWT"}""";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Mints a token for <paramref name="repo"/> alone: contents write when <paramref name="write"/> is true, read otherwise. A token
    /// with write access also holds workflows write when the installation of the App holds it, so a push of a change under
    /// <c>.github/workflows/</c> passes.
    /// </summary>
    public async Task<string> MintAsync(string repo, bool write, CancellationToken cancellationToken = default)
    {
        string path = $"/app/installations/{installationId}/access_tokens";
        using HttpClient http = Client();
        var client = new GitHubClient(http, CreateJwt(), baseWait);
        var permissions = new Dictionary<string, string> { ["contents"] = write ? "write" : "read", ["metadata"] = "read" };
        if (write && await HoldsWorkflowsWriteAsync(client, cancellationToken).ConfigureAwait(false))
        {
            permissions["workflows"] = "write";
        }

        string body = JsonSerializer.Serialize(new { repositories = new[] { repo }, permissions });
        JsonElement answer = await client.SendAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
        return answer.ValueKind is JsonValueKind.Object && answer.TryGetProperty("token", out JsonElement token) && token.GetString() is string text
            ? text
            : throw new GitHubRequestException("POST", path, 201, "the answer holds no token");
    }

    /// <summary>Revokes <paramref name="token"/> with <c>DELETE /installation/token</c>.</summary>
    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        using HttpClient http = Client();
        var client = new GitHubClient(http, token, baseWait);
        await client.SendAsync(HttpMethod.Delete, "/installation/token", null, cancellationToken).ConfigureAwait(false);
    }

    // The caller owns the handler, so the client leaves it open, and a client for each call costs no connection.
    private HttpClient Client() => new(handler, disposeHandler: false);

    /// <summary>Reads <c>permissions.workflows</c> of the installation with the JWT, and gives back whether it is <c>write</c>.</summary>
    private async Task<bool> HoldsWorkflowsWriteAsync(GitHubClient client, CancellationToken cancellationToken)
    {
        JsonElement installation = await client.SendAsync(HttpMethod.Get, $"/app/installations/{installationId}", null, cancellationToken).ConfigureAwait(false);
        return installation.ValueKind is JsonValueKind.Object
            && installation.TryGetProperty("permissions", out JsonElement permissions)
            && permissions.ValueKind is JsonValueKind.Object
            && permissions.TryGetProperty("workflows", out JsonElement workflows)
            && workflows.ValueKind is JsonValueKind.String
            && workflows.GetString() is "write";
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
