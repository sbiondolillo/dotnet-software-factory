namespace Core;

/// <summary>A request to run a program with arguments, an optional working
/// directory, extra environment variables and a timeout.</summary>
public sealed record CommandRequest
{
    public required string Program { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public TimeSpan? Timeout { get; init; }
}
