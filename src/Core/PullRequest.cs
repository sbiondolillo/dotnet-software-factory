namespace Core;

/// <summary>A pull request: its number and its web address.</summary>
public sealed record PullRequest(int Number, string HtmlUrl);
