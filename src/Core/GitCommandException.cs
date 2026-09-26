namespace Core;

/// <summary>Thrown when a git command exits with a code other than 0.</summary>
public sealed class GitCommandException(string subcommand, string standardError)
    : Exception($"git {subcommand} failed: {standardError}")
{
    /// <summary>The git subcommand that exited with a code other than 0.</summary>
    public string Subcommand { get; } = subcommand;

    /// <summary>What the command wrote to standard error.</summary>
    public string StandardError { get; } = standardError;
}
