using System.Text.Json;
using Core;

namespace Specs.Unit;

/// <summary>
/// The one test double of <see cref="AgentBackend"/>. Its builder and its reviewer are two <see cref="FakeAgent"/>s, so a run test
/// scripts the agents by phase and starts no <c>claude</c>. It has two starts.
/// <list type="bullet">
/// <item><see cref="Starter"/> starts a <see cref="Core.Sandbox"/> for the gates over a mock runner, as the default adapter does,
/// with the line <c>sandbox: start</c>. Its <see cref="Sessions"/> list is the one a test gives, empty by default, and its collect
/// step does nothing.</item>
/// <item><see cref="Around"/> starts a real adapter and puts the two fakes behind it: the adapter carries each prompt to its fake
/// API, and the fake of the prompt's role answers it. A test of the adapter's own work through a whole run uses it, such as the
/// branch work of <see cref="ManagedBackend"/>.</item>
/// </list>
/// Each answer of the first start goes to the run's <see cref="BackendStart.OnAnswer"/> with the time it took on the run's clock,
/// as an adapter reports it.
/// </summary>
public sealed class FakeBackend : AgentBackend
{
    private readonly IReadOnlyList<SessionCost> _sessions;
    private readonly AgentBackend? _real;

    private FakeBackend(Sandbox sandbox, Agent builder, Agent reviewer, IReadOnlyList<SessionCost> sessions, AgentBackend? real)
    {
        Sandbox = sandbox;
        Builder = builder;
        Reviewer = reviewer;
        _sessions = sessions;
        _real = real;
    }

    /// <inheritdoc/>
    public override Sandbox Sandbox { get; }

    /// <inheritdoc/>
    public override Agent Builder { get; }

    /// <inheritdoc/>
    public override Agent Reviewer { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SessionCost> Sessions => _real?.Sessions ?? _sessions;

    /// <summary>The start of a backend whose agents are <paramref name="builder"/> and <paramref name="reviewer"/>. It writes the
    /// progress line <c>sandbox: start</c>, then starts the sandbox over the clone with <paramref name="runner"/>.</summary>
    public static StartBackend Starter(
        CommandRunner runner,
        string imageTag,
        string rootDirectory,
        FakeAgent builder,
        FakeAgent reviewer,
        IReadOnlyList<SessionCost>? sessions = null,
        TimeProvider? time = null,
        TextWriter? progress = null) =>
        async (start, cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(start);
            if (progress is not null)
            {
                await progress.WriteLineAsync("sandbox: start");
                await progress.FlushAsync(cancellationToken);
            }
            Sandbox sandbox = await Sandbox.StartAsync(runner, imageTag, rootDirectory, start.Directory, cancellationToken);
            TimeProvider clock = time ?? TimeProvider.System;
            return new FakeBackend(
                sandbox, new Role(builder, null, start.OnAnswer, clock), new Role(reviewer, null, start.OnAnswer, clock), sessions ?? [], null);
        };

    /// <summary>The start of the real adapter that <paramref name="real"/> starts, with <paramref name="builder"/> behind its
    /// builder and <paramref name="reviewer"/> behind its reviewer. The adapter keeps its sandbox, its sessions and its collect
    /// step, and reports each answer itself.</summary>
    public static StartBackend Around(StartBackend real, FakeAgent builder, FakeAgent reviewer) =>
        async (start, cancellationToken) =>
        {
            AgentBackend inner = await real(start, cancellationToken);
            return new FakeBackend(
                inner.Sandbox, new Role(builder, inner.Builder, null, TimeProvider.System), new Role(reviewer, inner.Reviewer, null, TimeProvider.System), [], inner);
        };

    /// <summary>The answer of <c>claude</c> for the prompt that <paramref name="builder"/> or <paramref name="reviewer"/> has
    /// pending: one JSON object of the answer that the fake gives.</summary>
    public static CommandResult PlayClaude(FakeAgent builder, FakeAgent reviewer)
    {
        AgentAnswer answer = PendingOf(builder, reviewer).Play();
        string json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["is_error"] = answer.IsError,
            ["result"] = answer.Result,
            ["num_turns"] = answer.NumTurns,
            ["total_cost_usd"] = answer.CostUsd,
        });
        return new CommandResult { StandardOutput = json + "\n" };
    }

    /// <summary>The answer of a session of <paramref name="managed"/> for the prompt that <paramref name="builder"/> or
    /// <paramref name="reviewer"/> has pending. Each output of the answer goes to the files of the session. A build pushes a
    /// commit unless an act of the build called <see cref="ManagedRunFake.HoldHead"/>.</summary>
    public static ManagedTurn PlaySession(FakeAgent builder, FakeAgent reviewer, ManagedRunFake managed)
    {
        ArgumentNullException.ThrowIfNull(managed);
        FakeAgent agent = PendingOf(builder, reviewer);
        Phase phase = agent.Pending!.Phase;
        AgentAnswer answer = agent.Play();
        foreach ((string name, string text) in answer.Outputs)
        {
            managed.AddOutput(name, text);
        }

        int cents = (int)decimal.Round(answer.CostUsd * 100m, MidpointRounding.AwayFromZero);
        return new ManagedTurn(answer.Result, answer.IsError, cents, Pushes: phase is Phase.Build && !managed.TakeHold());
    }

    /// <inheritdoc/>
    public override Task CollectAsync(string branch, CancellationToken cancellationToken = default) =>
        _real?.CollectAsync(branch, cancellationToken) ?? Task.CompletedTask;

    /// <summary>Ends the real adapter, or removes the sandbox of the fake start.</summary>
    public override ValueTask DisposeAsync() => _real?.DisposeAsync() ?? Sandbox.DisposeAsync();

    private static FakeAgent PendingOf(FakeAgent builder, FakeAgent reviewer) =>
        builder.Pending is not null ? builder
        : reviewer.Pending is not null ? reviewer
        : throw new InvalidOperationException("The adapter sent a prompt with no request pending.");

    /// <summary>The builder or the reviewer of the backend: the fake alone, timed on the run's clock, or the fake behind the real
    /// agent of its role.</summary>
    private sealed class Role(FakeAgent fake, Agent? real, Action<TimeSpan, AgentAnswer>? onAnswer, TimeProvider clock) : Agent
    {
        public override async Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
        {
            if (real is not null)
            {
                fake.Pending = request;
                try
                {
                    return await real.RunAsync(request, cancellationToken);
                }
                finally
                {
                    fake.Pending = null;
                }
            }

            long started = clock.GetTimestamp();
            AgentAnswer answer = await fake.RunAsync(request, cancellationToken);
            onAnswer?.Invoke(clock.GetElapsedTime(started), answer);
            return answer;
        }
    }
}
