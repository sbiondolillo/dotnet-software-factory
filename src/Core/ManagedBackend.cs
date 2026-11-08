using System.Diagnostics.CodeAnalysis;

namespace Core;

/// <summary>
/// The backend of Managed Agents: the builder and the reviewer are two sessions over a scratch branch, and the gates run in a
/// sandbox that holds no key. The host pushes the scratch branch before each prompt, pulls it after each prompt of the builder, and
/// deletes it on origin at the end.
/// </summary>
public sealed class ManagedBackend : AgentBackend
{
    private readonly Config _config;
    private readonly BackendStart _start;
    private readonly ManagedAgentsAgent _builder;
    private readonly ManagedAgentsAgent _reviewer;
    private readonly HttpClient _http;
    private readonly HttpMessageHandler? _ownHandler;
    private bool _pushed;

    private ManagedBackend(Config config, BackendStart start, Sandbox sandbox)
    {
        _config = config;
        _start = start;
        Sandbox = sandbox;
        ManagedAgentsSettings settings = config.Settings;
        HttpMessageHandler handler = config.Handler ?? (_ownHandler = new SocketsHttpHandler());
        _http = new HttpClient(handler, disposeHandler: false);
        var tokens = new GitHubAppTokens(handler, settings.AppId, settings.InstallationId, settings.PrivateKeyPem, config.Time);
        var client = new ManagedAgentsClient(_http, settings.ApiKey, config.Delay)
        {
            AgentId = settings.AgentId,
            EnvironmentId = settings.EnvironmentId,
        };
        _builder = Session(client, tokens, SessionRole.Builder, settings.BuilderBudgetCents);
        _reviewer = Session(client, tokens, SessionRole.Reviewer, settings.ReviewBudgetCents);
        Builder = new BranchAgent(this, _builder, pulls: true);
        Reviewer = new BranchAgent(this, _reviewer, pulls: false);
    }

    /// <inheritdoc/>
    public override Sandbox Sandbox { get; }

    /// <inheritdoc/>
    public override Agent Builder { get; }

    /// <inheritdoc/>
    public override Agent Reviewer { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SessionCost> Sessions => [.. _builder.Sessions, .. _reviewer.Sessions];

    /// <summary>The start of this backend from the config known before the run. The start writes the progress line
    /// <c>sessions: start</c>, starts the sandbox of the gates over the clone, and builds the two sessions' agents.</summary>
    public static StartBackend Starter(
        CommandRunner runner,
        string imageTag,
        string rootDirectory,
        ManagedAgentsSettings settings,
        RepoName repo,
        string token,
        string authorName,
        string authorEmail,
        HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TextWriter? log = null,
        TimeProvider? time = null,
        TextWriter? progress = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var config = new Config(runner, imageTag, rootDirectory, settings, repo, token, authorName, authorEmail, handler, delay, log, time, progress);
        return (start, cancellationToken) => StartAsync(config, start, cancellationToken);
    }

    /// <summary>Puts the session's commits on <paramref name="branch"/>, for the push that delivers them.</summary>
    public override async Task CollectAsync(string branch, CancellationToken cancellationToken = default)
    {
        await _start.Git.CheckoutExistingBranchAsync(branch, _start.Directory, cancellationToken).ConfigureAwait(false);
        await _start.Git.PullAsync(_start.ScratchBranch, _start.Directory, _config.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ends both sessions, deletes the scratch branch on origin and removes the sandbox. A failed delete is logged.</summary>
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await _builder.DisposeAsync().ConfigureAwait(false);
            await _reviewer.DisposeAsync().ConfigureAwait(false);
            if (_pushed)
            {
                try
                {
                    await _start.Git.DeleteRemoteBranchAsync(_start.ScratchBranch, _start.Directory, _config.Token, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is GitCommandException or CommandStartException)
                {
                    await ProgressLine.WriteAsync(_config.Log, $"sessions: cannot delete the branch {_start.ScratchBranch}: {exception.Message}").ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _http.Dispose();
            _ownHandler?.Dispose();
            await Sandbox.DisposeAsync().ConfigureAwait(false);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The backend that the method gives back owns the sandbox, and its DisposeAsync removes it.")]
    private static async Task<AgentBackend> StartAsync(Config config, BackendStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        await ProgressLine.WriteAsync(config.Progress, "sessions: start").ConfigureAwait(false);
        Sandbox gates = await Sandbox.StartForGatesAsync(config.Runner, config.ImageTag, config.RootDirectory, start.Directory, cancellationToken).ConfigureAwait(false);
        return new ManagedBackend(config, start, gates);
    }

    private ManagedAgentsAgent Session(ManagedAgentsClient client, GitHubAppTokens tokens, SessionRole role, int budgetCents) => new(
        client,
        tokens,
        _config.Repo,
        _start.ScratchBranch,
        _start.BaseCommit,
        _start.Directory,
        _config.RootDirectory,
        role,
        budgetCents,
        _config.Log,
        _config.Time,
        _start.OnAnswer);

    /// <summary>
    /// Pushes the scratch branch, so the session builds on every commit of the host, such as the commit of the lint gate. The first
    /// call checks out the scratch branch and commits a change of the work tree, such as the scenarios that FactoryRun applied. The
    /// base commit stays as it was, so the diff against the base still holds that change. A push with no new commit changes nothing.
    /// </summary>
    private async Task PushScratchAsync(CancellationToken cancellationToken)
    {
        if (!_pushed)
        {
            await _start.Git.CheckoutBranchAsync(_start.ScratchBranch, _start.Directory, cancellationToken).ConfigureAwait(false);
            if (await _start.Git.HasChangesAsync(_start.Directory, cancellationToken).ConfigureAwait(false))
            {
                await _start.Git.CommitAllAsync(
                    _start.Directory, $"{_start.Title}: the scenarios of the issue", _config.AuthorName, _config.AuthorEmail, cancellationToken).ConfigureAwait(false);
            }
        }

        await _start.Git.PushAsync(_start.ScratchBranch, _start.Directory, _config.Token, cancellationToken).ConfigureAwait(false);
        _pushed = true;
    }

    /// <summary>The facts of the managed path known before the run, which <see cref="Starter"/> takes.</summary>
    private sealed record Config(
        CommandRunner Runner,
        string ImageTag,
        string RootDirectory,
        ManagedAgentsSettings Settings,
        RepoName Repo,
        string Token,
        string AuthorName,
        string AuthorEmail,
        HttpMessageHandler? Handler,
        Func<TimeSpan, CancellationToken, Task>? Delay,
        TextWriter? Log,
        TimeProvider? Time,
        TextWriter? Progress);

    /// <summary>
    /// A session agent with the branch work of the host around each prompt: the push of the scratch branch before each prompt, and
    /// for the builder the pull after each prompt, the check that no commit tracks a file under <c>.factory/</c>, and the read of the
    /// remote head around each prompt of <see cref="Phase.Build"/>, whose answer gains the fault <see cref="NoCommitText"/> when the
    /// head did not move. A tracked file under <c>.factory/</c>, before a prompt or after a pull, throws
    /// <see cref="SessionBranchException"/>, which stops the run whatever the phase. The session uploads what <c>.factory/</c> holds and
    /// follows a link there, so a head branch that tracks a file there stops before the first session starts.
    /// </summary>
    private sealed class BranchAgent(ManagedBackend owner, ManagedAgentsAgent inner, bool pulls) : Agent
    {
        /// <summary>The fault of a build after which the head of the scratch branch on origin did not move.</summary>
        public const string NoCommitText = "The build pushed no commit to the session branch. Commit the work and push it to that branch.";

        public override async Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            BackendStart start = owner._start;
            string token = owner._config.Token;
            await owner.PushScratchAsync(cancellationToken).ConfigureAwait(false);
            await RefuseTrackedFilesAsync(start, cancellationToken).ConfigureAwait(false);
            bool build = pulls && request.Phase is Phase.Build;
            string? before = build
                ? await start.Git.ReadRemoteHeadAsync(start.ScratchBranch, start.Directory, token, cancellationToken).ConfigureAwait(false)
                : null;
            AgentAnswer answer = await inner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (!pulls)
            {
                return answer;
            }

            await start.Git.PullAsync(start.ScratchBranch, start.Directory, token, cancellationToken).ConfigureAwait(false);
            if (build)
            {
                string? after = await start.Git.ReadRemoteHeadAsync(start.ScratchBranch, start.Directory, token, cancellationToken).ConfigureAwait(false);
                if (after == before)
                {
                    answer = answer with { Faults = [.. answer.Faults, NoCommitText] };
                }
            }

            await RefuseTrackedFilesAsync(start, cancellationToken).ConfigureAwait(false);
            return answer;
        }

        private static async Task RefuseTrackedFilesAsync(BackendStart start, CancellationToken cancellationToken)
        {
            string[] tracked = await start.Git.ListTrackedAsync(".factory", start.Directory, cancellationToken).ConfigureAwait(false);
            if (tracked.Length is not 0)
            {
                throw new SessionBranchException(
                    $"The session committed files under .factory/, which only the factory writes: {string.Join(", ", tracked)}. The run stops.");
            }
        }
    }
}
