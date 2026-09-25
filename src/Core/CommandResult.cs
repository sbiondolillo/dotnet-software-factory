namespace Core;

/// <summary>What a finished program returned.</summary>
public record CommandResult(int ExitCode, string StandardOutput, string StandardError);
