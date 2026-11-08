namespace Core;

/// <summary>A repo on GitHub: its owner and its name.</summary>
public sealed record RepoName(string Owner, string Name);

/// <summary>One issue or pull request of a repo: GitHub shares the number space.</summary>
public sealed record IssueNumber(RepoName Repo, int Number);
