using System.Diagnostics.CodeAnalysis;

namespace Core;

/// <summary>
/// What <see cref="RunAgents.StartAsync"/> needs: the clone, the branches, the path and what each agent reports to. The title and the
/// author name the commit of a work tree change before the push of the scratch branch.
/// </summary>
internal sealed record RunAgentsSetup(
    CommandRunner Runner,
    Git Git,
    string Owner,
    string Repo,
    string Token,
    string Directory,
    string RootDirectory,
    string ImageTag,
    string BaseCommit,
    string ScratchBranch,
    string Title,
    string AuthorName,
    string AuthorEmail,
    ManagedAgentsSettings? Settings,
    HttpMessageHandler? Handler = null,
    Func<TimeSpan, CancellationToken, Task>? Delay = null,
    TimeProvider? Time = null,
    TextWriter? Progress = null,
    TextWriter? Log = null,
    Action<TimeSpan, ClaudeCodeResult>? OnAnswer = null);

/// <summary>
/// The agents and the sandbox of one run. With no settings, both agents are Claude Code in one <see cref="Core.Sandbox"/>, and the
/// gates run in it. With settings, the builder and the reviewer are two Managed Agents sessions over a scratch branch, and the gates
/// run in a sandbox that holds no key. The host pushes the scratch branch before the first prompt, pulls it after each prompt of the
/// builder, and deletes it on origin at the end.
/// </summary>
internal sealed class RunAgents : IAsyncDisposable
{
    /// <summary>The Redo text of a build after which the head of the scratch branch on origin did not move.</summary>
    public const string NoCommitText = "The build pushed no commit to the session branch. Commit the work and push it to that branch.";

    private readonly RunAgentsSetup _setup;
    private readonly ManagedAgentsAgent? _builder;
    private readonly ManagedAgentsAgent? _reviewer;
    private readonly HttpClient? _http;
    private readonly HttpMessageHandler? _ownHandler;
    private bool _pushed;
    private bool _pushedNothing;

    private RunAgents(RunAgentsSetup setup, Sandbox sandbox)
    {
        _setup = setup;
        Sandbox = sandbox;
        var agent = new ClaudeCodeAgent(sandbox, setup.Log, setup.Time, setup.OnAnswer);
        Builder = agent;
        Reviewer = agent;
    }

    private RunAgents(RunAgentsSetup setup, ManagedAgentsSettings settings, Sandbox sandbox)
    {
        _setup = setup;
        Sandbox = sandbox;
        HttpMessageHandler handler = setup.Handler ?? (_ownHandler = new SocketsHttpHandler());
        _http = new HttpClient(handler, disposeHandler: false);
        var tokens = new GitHubAppTokens(handler, settings.AppId, settings.InstallationId, settings.PrivateKeyPem, setup.Time);
        var client = new ManagedAgentsClient(_http, settings.ApiKey, setup.Delay)
        {
            AgentId = settings.AgentId,
            EnvironmentId = settings.EnvironmentId,
        };
        _builder = Session(client, tokens, SessionRole.Builder, settings.BuilderBudgetCents);
        _reviewer = Session(client, tokens, SessionRole.Reviewer, settings.ReviewBudgetCents);
        Builder = new BranchAgent(this, _builder, pulls: true);
        Reviewer = new BranchAgent(this, _reviewer, pulls: false);
    }

    /// <summary>The container the gates run their commands in.</summary>
    public Sandbox Sandbox { get; }

    /// <summary>The agent of the plan and the build prompts.</summary>
    public Agent Builder { get; }

    /// <summary>The agent of the review prompts.</summary>
    public Agent Reviewer { get; }

    /// <summary>The Managed Agents sessions of the run, the builder's first; none for Claude Code.</summary>
    public IReadOnlyList<SessionCost> Sessions => _builder is null || _reviewer is null ? [] : [.. _builder.Sessions, .. _reviewer.Sessions];

    /// <summary>Starts the sandbox, and builds the two sessions' agents with settings, after the progress line of the path.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The RunAgents that the method gives back owns the sandbox, and its DisposeAsync removes it.")]
    public static async Task<RunAgents> StartAsync(RunAgentsSetup setup, CancellationToken cancellationToken = default)
    {
        if (setup.Settings is ManagedAgentsSettings settings)
        {
            await ProgressLine.WriteAsync(setup.Progress, "sessions: start").ConfigureAwait(false);
            Sandbox gates = await Sandbox.StartForGatesAsync(setup.Runner, setup.ImageTag, setup.RootDirectory, setup.Directory, cancellationToken).ConfigureAwait(false);
            return new RunAgents(setup, settings, gates);
        }

        await ProgressLine.WriteAsync(setup.Progress, "sandbox: start").ConfigureAwait(false);
        Sandbox sandbox = await Sandbox.StartAsync(setup.Runner, setup.ImageTag, setup.RootDirectory, setup.Directory, cancellationToken).ConfigureAwait(false);
        return new RunAgents(setup, sandbox);
    }

    /// <summary>The lint gate over <see cref="Sandbox"/>.</summary>
    public LintGate Lint(TextWriter? log) => new(Sandbox, _setup.Git, log, restoreFirst: _builder is not null);

    /// <summary>Pushes the commit of the lint gate to the scratch branch, so the next session message builds on it.</summary>
    public async Task AfterLintAsync(bool committed, CancellationToken cancellationToken = default)
    {
        if (_builder is not null && committed)
        {
            await _setup.Git.PushAsync(_setup.ScratchBranch, _setup.Directory, _setup.Token, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Adds <see cref="NoCommitText"/> to the Redo texts when the last build pushed no commit.</summary>
    public IReadOnlyList<string> NoteBuild(IReadOnlyList<string> redoTexts) =>
        _pushedNothing && !redoTexts.Contains(NoCommitText) ? [.. redoTexts, NoCommitText] : redoTexts;

    /// <summary>Checks out <paramref name="branch"/> and fast-forwards it to the scratch branch, so a push of it sends the session's work.</summary>
    public async Task MoveToAsync(string branch, CancellationToken cancellationToken = default)
    {
        if (_builder is null)
        {
            return;
        }

        await _setup.Git.CheckoutExistingBranchAsync(branch, _setup.Directory, cancellationToken).ConfigureAwait(false);
        await _setup.Git.PullAsync(_setup.ScratchBranch, _setup.Directory, _setup.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ends both sessions, deletes the scratch branch on origin and removes the sandbox. A failed delete is logged.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_builder is not null)
            {
                await _builder.DisposeAsync().ConfigureAwait(false);
            }

            if (_reviewer is not null)
            {
                await _reviewer.DisposeAsync().ConfigureAwait(false);
            }

            if (_pushed)
            {
                try
                {
                    await _setup.Git.DeleteRemoteBranchAsync(_setup.ScratchBranch, _setup.Directory, _setup.Token, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is GitCommandException or CommandStartException)
                {
                    await ProgressLine.WriteAsync(_setup.Log, $"sessions: cannot delete the branch {_setup.ScratchBranch}: {exception.Message}").ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _http?.Dispose();
            _ownHandler?.Dispose();
            await Sandbox.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ManagedAgentsAgent Session(ManagedAgentsClient client, GitHubAppTokens tokens, SessionRole role, int budgetCents) => new(
        client,
        tokens,
        _setup.Owner,
        _setup.Repo,
        _setup.ScratchBranch,
        _setup.BaseCommit,
        _setup.Directory,
        _setup.RootDirectory,
        role,
        budgetCents,
        _setup.Log,
        _setup.Time,
        _setup.OnAnswer);

    /// <summary>
    /// Checks out the scratch branch, commits a change of the work tree, such as the scenarios that FactoryRun applied, and pushes the
    /// branch once. The base commit stays as it was, so the diff against the base still holds that change.
    /// </summary>
    private async Task PushScratchAsync(CancellationToken cancellationToken)
    {
        if (_pushed)
        {
            return;
        }

        await _setup.Git.CheckoutBranchAsync(_setup.ScratchBranch, _setup.Directory, cancellationToken).ConfigureAwait(false);
        if (await _setup.Git.HasChangesAsync(_setup.Directory, cancellationToken).ConfigureAwait(false))
        {
            await _setup.Git.CommitAllAsync(
                _setup.Directory, $"{_setup.Title}: the scenarios of the issue", _setup.AuthorName, _setup.AuthorEmail, cancellationToken).ConfigureAwait(false);
        }

        await _setup.Git.PushAsync(_setup.ScratchBranch, _setup.Directory, _setup.Token, cancellationToken).ConfigureAwait(false);
        _pushed = true;
    }

    /// <summary>
    /// A session agent with the branch work of the host around each prompt: the push of the scratch branch before the first prompt, and
    /// for the builder the pull after each prompt, the check that no commit tracks a file under <c>.factory/</c>, and the read of the
    /// remote head around each build prompt. A tracked file under <c>.factory/</c> throws <see cref="SessionBranchException"/>, which
    /// stops the run whatever the phase.
    /// </summary>
    private sealed class BranchAgent(RunAgents owner, ManagedAgentsAgent inner, bool pulls) : Agent
    {
        public override async Task<ClaudeCodeResult> RunAsync(
            string prompt,
            string model,
            int maxTurns,
            string phase,
            CancellationToken cancellationToken = default)
        {
            RunAgentsSetup setup = owner._setup;
            await owner.PushScratchAsync(cancellationToken).ConfigureAwait(false);
            bool build = pulls && phase is "build";
            string? before = build
                ? await setup.Git.ReadRemoteHeadAsync(setup.ScratchBranch, setup.Directory, setup.Token, cancellationToken).ConfigureAwait(false)
                : null;
            ClaudeCodeResult answer = await inner.RunAsync(prompt, model, maxTurns, phase, cancellationToken).ConfigureAwait(false);
            if (!pulls)
            {
                return answer;
            }

            await setup.Git.PullAsync(setup.ScratchBranch, setup.Directory, setup.Token, cancellationToken).ConfigureAwait(false);
            if (build)
            {
                string? after = await setup.Git.ReadRemoteHeadAsync(setup.ScratchBranch, setup.Directory, setup.Token, cancellationToken).ConfigureAwait(false);
                owner._pushedNothing = after == before;
            }

            string[] tracked = await setup.Git.ListTrackedAsync(".factory", setup.Directory, cancellationToken).ConfigureAwait(false);
            return tracked.Length is 0
                ? answer
                : throw new SessionBranchException(
                    $"The session committed files under .factory/, which only the factory writes: {string.Join(", ", tracked)}. The run stops.");
        }
    }
}
