namespace Core;

/// <summary>The result of a program CommandRunner ran: its exit code, its standard output and its standard error.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
