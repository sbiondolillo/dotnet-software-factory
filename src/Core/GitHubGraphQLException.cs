namespace Core;

/// <summary>Thrown when GitHub's GraphQL API answers with status 200 and a list of errors.</summary>
public sealed class GitHubGraphQLException(IReadOnlyList<string> messages)
    : Exception("GitHub GraphQL request failed: " + string.Join("; ", messages))
{
    /// <summary>The message of each error, in the order GitHub gave them.</summary>
    public IReadOnlyList<string> Messages { get; } = messages;
}
