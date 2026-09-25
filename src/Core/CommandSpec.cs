namespace Core;

/// <summary>Describes the program a <see cref="CommandRunner"/> runs.</summary>
public sealed class CommandSpec
{
    /// <summary>The program to run.</summary>
    public required string Command { get; init; }

    /// <summary>The arguments, in order.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The directory the program runs in, or null for the caller's directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Extra environment variables, overlaid on the caller's environment.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];

    /// <summary>The time the program is given to finish, or null for no limit.</summary>
    public TimeSpan? Timeout { get; init; }
}
