namespace Core;

/// <summary>Thrown when a <see cref="ClaudeCodeAgent"/> run cannot start or ends with no JSON object.</summary>
public sealed class ClaudeCodeException(string message) : Exception(message)
{
}
