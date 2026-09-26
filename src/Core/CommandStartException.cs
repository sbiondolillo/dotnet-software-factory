namespace Core;

/// <summary>Thrown when a <see cref="CommandRunner"/> cannot start the program it was asked to run.</summary>
public sealed class CommandStartException : Exception
{
    /// <summary>Creates the exception for a failed start of <paramref name="command"/>.</summary>
    public CommandStartException(string command, Exception innerException)
        : base($"could not start {command}: {innerException.Message}", innerException)
    {
    }
}
