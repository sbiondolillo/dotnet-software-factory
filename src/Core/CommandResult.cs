namespace Core;

/// <summary>The captured result of a program a <see cref="CommandRunner"/> ran.</summary>
public sealed class CommandResult
{
    /// <summary>The program's exit code.</summary>
    public int ExitCode { get; init; }

    /// <summary>Everything the program wrote to standard output.</summary>
    public string StandardOutput { get; init; } = "";

    /// <summary>Everything the program wrote to standard error.</summary>
    public string StandardError { get; init; } = "";
}
