namespace Core;

/// <summary>The gates of a run. The caller names the row and the progress lines of a gate after its name.</summary>
public enum GateName
{
    Scope,
    Test,
    Api,
    Check,
}

/// <summary>What a gate found: <paramref name="Result"/> is the text of its row, and <paramref name="RedoTexts"/> is empty on a pass.</summary>
public sealed record GateOutcome(string Result, IReadOnlyList<string> RedoTexts);

/// <summary>A deterministic test of a build. A gate never stops the run, and it writes no row and no progress line.</summary>
public interface IGate
{
    /// <summary>The name of the gate, which names its row and its progress lines.</summary>
    GateName Name { get; }

    /// <summary>Runs the gate over the work tree as it is now.</summary>
    Task<GateOutcome> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>The text of a <see cref="GateName"/> in a row and a progress line.</summary>
public static class GateNames
{
    /// <summary>Gives <c>scope</c>, <c>test</c>, <c>api</c> or <c>check</c>.</summary>
    public static string Text(this GateName name) => name switch
    {
        GateName.Scope => "scope",
        GateName.Test => "test",
        GateName.Api => "api",
        GateName.Check => "check",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "The gate has no text."),
    };
}
