namespace Core;

/// <summary>The scope gate of a cleanup or a reframe: each path changed since <paramref name="baseCommit"/> is in the scope,
/// and none is a feature file.</summary>
public sealed class ScopeGate(Git git, string baseCommit, string workTree, IReadOnlyList<string> scope) : IGate
{
    /// <inheritdoc/>
    public GateName Name => GateName.Scope;

    /// <summary>Gives <c>in scope</c>, or <c>out of scope</c> with one Redo text for each path outside the scope or under the pin.
    /// A path under <c>.factory/</c> is no fault.</summary>
    public async Task<GateOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        string[] changed = await git.ListChangedFilesAsync(baseCommit, workTree, cancellationToken).ConfigureAwait(false);
        List<string> faults = [];
        foreach (string path in changed.Where(path => !path.StartsWith(".factory/", StringComparison.Ordinal)))
        {
            if (FeatureFiles.IsFeaturePath(path))
            {
                faults.Add($"pin: {path}");
            }
            else if (!CleanupBody.InScope(scope, path))
            {
                faults.Add($"outside the scope: {path}");
            }
        }

        return new GateOutcome(faults.Count is 0 ? "in scope" : "out of scope", faults);
    }
}
