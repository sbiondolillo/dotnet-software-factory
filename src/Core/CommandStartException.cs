namespace Core;

/// <summary>Thrown when a <see cref="CommandRunner"/> cannot start the program it was asked to run.</summary>
public sealed class CommandStartException(string command, Exception innerException)
    : Exception($"could not start {command}: {innerException.Message}", innerException)
{
}
