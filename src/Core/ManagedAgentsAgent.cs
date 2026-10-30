namespace Core;

/// <summary>Runs each prompt as one message of a Managed Agents session over a GitHub repository resource, and reads the answer
/// from the session's events. The session clones <paramref name="sessionBranch"/>, and the builder role pushes to it after each message.</summary>
public sealed class ManagedAgentsAgent(
    ManagedAgentsClient client,
    GitHubAppTokens tokens,
    Git git,
    string owner,
    string repo,
    string sessionBranch,
    string baseCommit,
    string workTree,
    string rootDirectory,
    SessionRole role,
    int budgetCents,
    TextWriter? log = null,
    TimeProvider? time = null,
    Action<TimeSpan, ClaudeCodeResult>? onAnswer = null) : Agent, IAsyncDisposable
{
    internal ManagedAgentsClient Client { get; } = client;

    internal GitHubAppTokens Tokens { get; } = tokens;

    internal Git Git { get; } = git;

    internal string Owner { get; } = owner;

    internal string Repo { get; } = repo;

    internal string SessionBranch { get; } = sessionBranch;

    internal string BaseCommit { get; } = baseCommit;

    internal string WorkTree { get; } = workTree;

    internal string RootDirectory { get; } = rootDirectory;

    internal SessionRole Role { get; } = role;

    internal int BudgetCents { get; } = budgetCents;

    internal TextWriter? Log { get; } = log;

    internal TimeProvider Time { get; } = time ?? TimeProvider.System;

    internal Action<TimeSpan, ClaudeCodeResult>? OnAnswer { get; } = onAnswer;

    /// <summary>The sessions this agent opened, with the cost each one reached.</summary>
    public IReadOnlyList<SessionCost> Sessions => throw new NotImplementedException();

    /// <inheritdoc />
    public override Task<ClaudeCodeResult> RunAsync(
        string prompt,
        string model,
        int maxTurns,
        string phase,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Interrupts a running session and revokes the App token.</summary>
    public ValueTask DisposeAsync() => throw new NotImplementedException();
}
