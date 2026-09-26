namespace Core;

/// <summary>Thrown when a git command exits with a code other than 0.</summary>
public sealed class GitCommandException(string subcommand, string standardError)
    : Exception($"git {subcommand} failed: {standardError}")
{
}
