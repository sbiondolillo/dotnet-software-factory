namespace Core;

/// <summary>Thrown when a git command a <see cref="Git"/> ran exits with a code other than 0.</summary>
public sealed class GitCommandException(string subcommand, string standardError)
    : Exception($"git {subcommand} failed: {standardError}")
{
}
