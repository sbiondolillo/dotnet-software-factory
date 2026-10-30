namespace Core;

/// <summary>Mints and revokes installation tokens of a GitHub App, with a JWT signed by the App's private key.</summary>
public sealed class GitHubAppTokens(
    HttpMessageHandler handler,
    string appId,
    string installationId,
    string privateKeyPem,
    TimeProvider? time = null)
{
    internal HttpMessageHandler Handler { get; } = handler;

    internal string AppId { get; } = appId;

    internal string InstallationId { get; } = installationId;

    internal string PrivateKeyPem { get; } = privateKeyPem;

    internal TimeProvider Time { get; } = time ?? TimeProvider.System;

    /// <summary>Mints a token for <paramref name="repo"/> alone: contents write when <paramref name="write"/> is true, read otherwise.</summary>
    public Task<string> MintAsync(string repo, bool write, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Revokes <paramref name="token"/> with <c>DELETE /installation/token</c>.</summary>
    public Task RevokeAsync(string token, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
