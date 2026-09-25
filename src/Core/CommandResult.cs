namespace Core;

/// <summary>The result of running a program: its exit code, standard output and standard error.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
