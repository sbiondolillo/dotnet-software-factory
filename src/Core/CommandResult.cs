namespace Core;

/// <summary>The outcome of a program started by <see cref="CommandRunner"/>.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
