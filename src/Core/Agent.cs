namespace Core;

/// <summary>Runs one prompt through an agent and gives its answer. <see cref="ClaudeCodeAgent"/> runs <c>claude</c> in a
/// <see cref="Sandbox"/>; <see cref="ManagedAgentsAgent"/> sends the prompt to a Managed Agents session.</summary>
public abstract class Agent
{
    /// <summary>Runs <paramref name="prompt"/> with <paramref name="model"/> and <paramref name="maxTurns"/>.</summary>
    public Task<ClaudeCodeResult> RunAsync(
        string prompt,
        string model,
        int maxTurns,
        CancellationToken cancellationToken = default) =>
        RunAsync(prompt, model, maxTurns, "agent", cancellationToken);

    /// <summary>Runs <paramref name="prompt"/> and logs its start and end lines under the name <paramref name="phase"/>.</summary>
    public abstract Task<ClaudeCodeResult> RunAsync(
        string prompt,
        string model,
        int maxTurns,
        string phase,
        CancellationToken cancellationToken = default);
}
