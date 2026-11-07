namespace Core;

/// <summary>The facts of a run known after the checkout: the clone, its directory, the base commit, the branch of the Managed
/// Agents sessions, the title of the commit of a work tree change, and what each answer reports to.</summary>
public sealed record BackendStart(
    Git Git,
    string Directory,
    string BaseCommit,
    string ScratchBranch,
    string Title,
    Action<TimeSpan, AgentAnswer>? OnAnswer);

/// <summary>Starts the backend of one run, after the checkout.</summary>
public delegate Task<AgentBackend> StartBackend(BackendStart start, CancellationToken cancellationToken);

/// <summary>The facts of a run known before it starts, which <see cref="AgentBackend.Pick"/> reads. With
/// <paramref name="Settings"/>, the run is on the Managed Agents path. The <paramref name="Handler"/> and the
/// <paramref name="Delay"/> serve that path alone.</summary>
public sealed record BackendOptions(
    CommandRunner Runner,
    string ImageTag,
    string RootDirectory,
    ManagedAgentsSettings? Settings,
    string Owner,
    string Repo,
    string Token,
    string AuthorName,
    string AuthorEmail,
    HttpMessageHandler? Handler = null,
    Func<TimeSpan, CancellationToken, Task>? Delay = null,
    TextWriter? Log = null,
    TimeProvider? Time = null,
    TextWriter? Progress = null);

/// <summary>
/// What a run gets from its adapter: the builder, the reviewer, the container the gates run in, the cost rows for the summary and
/// the collect step. The run sees the backend alone, and never the adapter. Its disposal ends the agents and removes the sandbox.
/// </summary>
public abstract class AgentBackend : IAsyncDisposable
{
    /// <summary>The container the gates run their commands in.</summary>
    public abstract Sandbox Sandbox { get; }

    /// <summary>The agent of the plan and the build prompts.</summary>
    public abstract Agent Builder { get; }

    /// <summary>The agent of the review prompts.</summary>
    public abstract Agent Reviewer { get; }

    /// <summary>The Managed Agents sessions of the run, the builder's first; none for Claude Code.</summary>
    public abstract IReadOnlyList<SessionCost> Sessions { get; }

    /// <summary>Puts the agents' commits on <paramref name="branch"/>, for the push that delivers them.</summary>
    public abstract Task CollectAsync(string branch, CancellationToken cancellationToken = default);

    /// <summary>Ends the agents and removes the sandbox.</summary>
    public abstract ValueTask DisposeAsync();

    /// <summary>The start of <see cref="ManagedBackend"/> when <paramref name="options"/> holds the managed settings, and of
    /// <see cref="ClaudeCodeBackend"/> when it holds none.</summary>
    public static StartBackend Pick(BackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Settings is ManagedAgentsSettings settings
            ? ManagedBackend.Starter(
                options.Runner, options.ImageTag, options.RootDirectory, settings, options.Owner, options.Repo, options.Token,
                options.AuthorName, options.AuthorEmail, options.Handler, options.Delay, options.Log, options.Time, options.Progress)
            : ClaudeCodeBackend.Starter(options.Runner, options.ImageTag, options.RootDirectory, options.Log, options.Time, options.Progress);
    }
}
