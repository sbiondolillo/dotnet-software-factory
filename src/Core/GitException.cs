namespace Core;

/// <summary>Thrown when a git command the factory ran exits with a non-zero code.</summary>
public sealed class GitException : Exception
{
    /// <summary>Creates the exception for a failed git <paramref name="subcommand"/>.</summary>
    public GitException(string subcommand, CommandResult result)
        : base($"git {subcommand} failed: {result.StandardError.Trim()}")
    {
    }
}
