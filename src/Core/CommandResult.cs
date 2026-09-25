namespace Core;

/// <summary>The outcome of a program <see cref="CommandRunner"/> ran.</summary>
public sealed class CommandResult
{
    /// <summary>Initializes the result with its exit code and captured streams.</summary>
    public CommandResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>The exit code the program returned.</summary>
    public int ExitCode { get; init; }

    /// <summary>The text the program wrote to standard output, with trailing line endings trimmed.</summary>
    public string StandardOutput { get; init; }

    /// <summary>The text the program wrote to standard error, with trailing line endings trimmed.</summary>
    public string StandardError { get; init; }
}
