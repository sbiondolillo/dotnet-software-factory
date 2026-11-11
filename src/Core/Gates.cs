namespace Core;

/// <summary>The gates of a run. The caller names the row and the progress lines of a gate after its name.</summary>
public enum GateName
{
    Scope,
    Test,
    Api,
    Check,
}

/// <summary>What a gate found: <paramref name="Result"/> is the text of its row, and <paramref name="RedoTexts"/> are the faults
/// that give a Redo. The build passes the gate when <paramref name="RedoTexts"/> is empty. The test gate gives a lost test as a
/// Redo text under the result <c>passed</c>, so a caller reads <paramref name="RedoTexts"/>, never <paramref name="Result"/>, to
/// decide on a Redo.</summary>
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

/// <summary>What <see cref="GateFactory.CreateAsync"/> gives: the gates in the order of the recipe, the lint gate that runs after
/// each build, or the text of the comment that stops the run before the first build.</summary>
public sealed record GateSet(IReadOnlyList<IGate> Gates, LintGate Lint, string? Stop);

/// <summary>Maps each <see cref="GateName"/> of a choice to its gate, over the work tree at <paramref name="baseCommit"/>, its
/// <paramref name="features"/> and its <paramref name="files"/>. <paramref name="progress"/> takes the progress lines of a gate that reads a baseline before the
/// first build.</summary>
public sealed class GateFactory(
    Sandbox sandbox,
    Git git,
    string workTree,
    FeatureFiles features,
    FileHashes files,
    string baseCommit,
    IReadOnlyList<string> secretNames,
    TextWriter? progress = null,
    TextWriter? log = null,
    bool skipIntegration = false)
{
    /// <summary>Gives a gate for each name of <see cref="RecipeSequence.Gates"/> of <paramref name="sequence"/>, in that order, and the lint gate. The test gate of a choice with a
    /// scope reads the baseline of its lost-test check. The API gate lists the public API of the base commit between
    /// <c>api: start</c> and <c>api: listed</c>, and a listing that fails gives the stop text and no gate.</summary>
    public async Task<GateSet> CreateAsync(RecipeChoice choice, RecipeSequence sequence, Pin pin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(sequence);
        var lint = new LintGate(sandbox, git, log);
        List<IGate> gates = [];
        foreach (GateName name in sequence.Gates)
        {
            switch (name)
            {
                case GateName.Scope:
                    gates.Add(new ScopeGate(git, baseCommit, workTree, choice.Scope));
                    break;
                case GateName.Test:
                    gates.Add(await TestGate
                        .CreateAsync(sandbox, workTree, features, files, pin, secretNames, choice.Scope.Count is 0 ? null : choice.Scope, log, skipIntegration, cancellationToken)
                        .ConfigureAwait(false));
                    break;
                case GateName.Api:
                    await ProgressLine.WriteAsync(progress, "api: start").ConfigureAwait(false);
                    ApiGateStart api = await ApiGate.CreateAsync(sandbox, log, cancellationToken).ConfigureAwait(false);
                    if (api.Gate is not ApiGate apiGate)
                    {
                        return new GateSet([], lint, api.Stop ?? "");
                    }

                    await ProgressLine.WriteAsync(progress, "api: listed").ConfigureAwait(false);
                    gates.Add(apiGate);
                    break;
                case GateName.Check:
                    gates.Add(new CheckGate(sandbox, choice.Check ?? throw new ArgumentException("The choice names the check gate and holds no check.", nameof(choice)), log));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(choice), name, "The gate has no factory.");
            }
        }

        return new GateSet(gates, lint, null);
    }
}
