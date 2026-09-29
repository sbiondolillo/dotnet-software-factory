namespace Core;

/// <summary>An issue as <see cref="GitHubClient.ReadIssueAsync"/> reads it.</summary>
public sealed record GitHubIssue(string Title, string Body, string Author, IReadOnlyList<string> Labels);
