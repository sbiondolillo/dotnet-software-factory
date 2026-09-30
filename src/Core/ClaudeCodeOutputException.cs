namespace Core;

/// <summary>Thrown when a file the agent wrote is missing or does not hold one JSON object.</summary>
public sealed class ClaudeCodeOutputException(string path, string reason)
    : Exception($"{path}: {reason}")
{
}
