namespace Core;

/// <summary>The build of a factory run: one prompt of <paramref name="phase"/> over the issue's <paramref name="title"/> and
/// <paramref name="body"/>, which asks for no output. An answer with <c>is_error</c> is the stop, and the build gives no Redo
/// text of its own. The faults of the answer pass through.</summary>
public sealed class FactoryBuild(BuildPhase phase, string title, string body, string model, int maxTurns) : IBuild
{
    /// <inheritdoc/>
    public async Task<BuildOutcome> RunAsync(IReadOnlyList<string> redoTexts, CancellationToken cancellationToken = default)
    {
        BuildPhaseResult built = await phase.RunAsync(title, body, model, maxTurns, redoTexts, [], cancellationToken).ConfigureAwait(false);
        return new BuildOutcome(built.Passed ? null : built.Failure, [], built.Faults);
    }
}
