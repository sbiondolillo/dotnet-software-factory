namespace Core;

/// <summary>The outcome of a command run with <see cref="CommandRunner"/>.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
