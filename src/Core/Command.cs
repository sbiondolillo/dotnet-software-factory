namespace Core;

/// <summary>A program to run: its name, arguments, working directory, extra environment and timeout.</summary>
public record Command(
    string Name,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? ExtraEnvironment = null,
    TimeSpan? Timeout = null);

/// <summary>The outcome of a run: the exit code and both streams, captured whole.</summary>
public record CommandResult(int ExitCode, string StandardOutput, string StandardError);
