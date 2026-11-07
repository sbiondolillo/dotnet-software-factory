using System.Diagnostics.CodeAnalysis;

namespace Core;

/// <summary>
/// The backend of Claude Code: one agent in one <see cref="Core.Sandbox"/>, which is both the builder and the reviewer. The gates
/// run in that sandbox, which holds the key and the packages that the agent restored. The work is on the branch of the clone, so
/// the collect step does nothing.
/// </summary>
public sealed class ClaudeCodeBackend : AgentBackend
{
    private readonly ClaudeCodeAgent _agent;

    private ClaudeCodeBackend(Sandbox sandbox, BackendStart start, TextWriter? log, TimeProvider? time)
    {
        Sandbox = sandbox;
        _agent = new ClaudeCodeAgent(sandbox, start.Directory, log, time, start.OnAnswer);
    }

    /// <inheritdoc/>
    public override Sandbox Sandbox { get; }

    /// <inheritdoc/>
    public override Agent Builder => _agent;

    /// <inheritdoc/>
    public override Agent Reviewer => _agent;

    /// <inheritdoc/>
    public override IReadOnlyList<SessionCost> Sessions => [];

    /// <summary>The start of this backend from the config known before the run. The start writes the progress line
    /// <c>sandbox: start</c>, then starts the sandbox over the clone.</summary>
    public static StartBackend Starter(
        CommandRunner runner,
        string imageTag,
        string rootDirectory,
        TextWriter? log = null,
        TimeProvider? time = null,
        TextWriter? progress = null) =>
        (start, cancellationToken) => StartAsync(runner, imageTag, rootDirectory, log, time, progress, start, cancellationToken);

    /// <inheritdoc/>
    public override Task CollectAsync(string branch, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Removes the sandbox.</summary>
    public override ValueTask DisposeAsync() => Sandbox.DisposeAsync();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The backend that the method gives back owns the sandbox, and its DisposeAsync removes it.")]
    private static async Task<AgentBackend> StartAsync(
        CommandRunner runner,
        string imageTag,
        string rootDirectory,
        TextWriter? log,
        TimeProvider? time,
        TextWriter? progress,
        BackendStart start,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        await ProgressLine.WriteAsync(progress, "sandbox: start").ConfigureAwait(false);
        Sandbox sandbox = await Sandbox.StartAsync(runner, imageTag, rootDirectory, start.Directory, cancellationToken).ConfigureAwait(false);
        return new ClaudeCodeBackend(sandbox, start, log, time);
    }
}
