using Core;

namespace Specs.Unit;

/// <summary>
/// The one test double of <see cref="AgentBackend"/>, for a test whose unit is the run. Its builder and its reviewer are two
/// <see cref="FakeAgent"/>s, so a run test scripts the agents by phase and starts no <c>claude</c>. Its sandbox of the gates is a
/// real <see cref="Core.Sandbox"/> over the harness's mock runner. Its <see cref="Sessions"/> list is the one a test gives, empty by
/// default, and its collect step does nothing. It hands each <see cref="BackendStart"/> it gets to its caller, so a run test reads the session
/// branch, the base commit and the title the run hands the backend. Each answer goes to the run's <see cref="BackendStart.OnAnswer"/> with the time it
/// took on the run's clock. What an adapter does below the seam, such as a progress line or a git command, it does not do: a test
/// of an adapter drives that adapter alone.
/// </summary>
public sealed class FakeBackend : AgentBackend
{
    private readonly IReadOnlyList<SessionCost> _sessions;
    private readonly Action<string>? _onStep;

    private FakeBackend(Sandbox sandbox, Agent builder, Agent reviewer, IReadOnlyList<SessionCost> sessions, Action<string>? onStep)
    {
        Sandbox = sandbox;
        Builder = builder;
        Reviewer = reviewer;
        _sessions = sessions;
        _onStep = onStep;
    }

    /// <inheritdoc/>
    public override Sandbox Sandbox { get; }

    /// <inheritdoc/>
    public override Agent Builder { get; }

    /// <inheritdoc/>
    public override Agent Reviewer { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SessionCost> Sessions => _sessions;

    /// <summary>The start of a backend whose agents are <paramref name="builder"/> and <paramref name="reviewer"/>, over a sandbox
    /// that <paramref name="runner"/> starts over the clone. <paramref name="onStart"/> gets the <see cref="BackendStart"/> of the
    /// run. <paramref name="onStep"/> gets <c>collect</c> and the branch at each collect step, so a run test reads its order against
    /// the run's own commands.</summary>
    public static StartBackend Starter(
        CommandRunner runner,
        string imageTag,
        string rootDirectory,
        FakeAgent builder,
        FakeAgent reviewer,
        IReadOnlyList<SessionCost>? sessions = null,
        TimeProvider? time = null,
        Action<string>? onStep = null,
        Action<BackendStart>? onStart = null) =>
        async (start, cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(start);
            onStart?.Invoke(start);
            Sandbox sandbox = await Sandbox.StartAsync(runner, imageTag, rootDirectory, start.Directory, cancellationToken);
            TimeProvider clock = time ?? TimeProvider.System;
            return new FakeBackend(
                sandbox, new Role(builder, start.OnAnswer, clock), new Role(reviewer, start.OnAnswer, clock), sessions ?? [], onStep);
        };

    /// <summary>Does nothing to the clone, and tells <c>onStep</c>.</summary>
    public override Task CollectAsync(string branch, CancellationToken cancellationToken = default)
    {
        _onStep?.Invoke($"collect {branch}");
        return Task.CompletedTask;
    }

    /// <summary>Removes the sandbox.</summary>
    public override ValueTask DisposeAsync() => Sandbox.DisposeAsync();

    /// <summary>The builder or the reviewer of the backend: the fake, timed on the run's clock, with each answer reported.</summary>
    private sealed class Role(FakeAgent fake, Action<TimeSpan, AgentAnswer>? onAnswer, TimeProvider clock) : Agent
    {
        public override async Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
        {
            long started = clock.GetTimestamp();
            AgentAnswer answer = await fake.RunAsync(request, cancellationToken);
            onAnswer?.Invoke(clock.GetElapsedTime(started), answer);
            return answer;
        }
    }
}
