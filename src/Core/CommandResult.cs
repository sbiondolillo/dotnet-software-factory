namespace Core;

/// <summary>The outcome of running a program to completion.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
