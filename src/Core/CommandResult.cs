namespace Core;

/// <summary>The outcome of running a command: its exit code and the two streams it wrote.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
