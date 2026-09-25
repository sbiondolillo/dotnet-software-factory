namespace Core;

/// <summary>Everything a caller asks <see cref="CommandRunner"/> to start.</summary>
public record CommandRequest(
    string Program,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    TimeSpan? Timeout = null);
