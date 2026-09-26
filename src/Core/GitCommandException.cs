namespace Core;

/// <summary>Thrown when a git subcommand a <see cref="Git"/> runs exits with a non-zero code.</summary>
public sealed class GitCommandException(string subcommand, string standardError)
    : Exception($"git {subcommand} failed: {standardError}")
{
}
