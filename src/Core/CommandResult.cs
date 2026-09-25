namespace Core;

/// <summary>The whole result of running a command: its exit code and its
/// standard output and standard error, captured entirely.</summary>
public sealed record CommandResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }
}
