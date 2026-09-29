namespace Core;

/// <summary>Thrown when GitHub answers a request with a status code outside 200 to 299.</summary>
public sealed class GitHubRequestException(string method, string path, int statusCode, string responseBody)
    : Exception($"GitHub request {method} {path} failed with status {statusCode}: {responseBody}")
{
    /// <summary>The status code of the answer.</summary>
    public int StatusCode { get; } = statusCode;
}
