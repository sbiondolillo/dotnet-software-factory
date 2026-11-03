namespace Core;

/// <summary>The phase of a run that a prompt serves.</summary>
public enum Phase
{
    Plan,
    Build,
    Review,
}

/// <summary>The names of <see cref="Phase"/> as the job summary and the progress lines write them.</summary>
public static class PhaseNames
{
    /// <summary>The name of <paramref name="phase"/> in lower case: <c>plan</c>, <c>build</c> or <c>review</c>.</summary>
    public static string Text(this Phase phase) => phase switch
    {
        Phase.Plan => "plan",
        Phase.Build => "build",
        Phase.Review => "review",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };
}

/// <summary>One prompt for an agent, the phase it serves, and the base names of the outputs the phase reads after it.</summary>
public sealed record AgentRequest(string Prompt, string Model, int MaxTurns, Phase Phase, IReadOnlyList<string> Outputs);

/// <summary>The answer of one prompt. Outputs holds the text of each expected output the agent wrote, by base name. Faults are
/// the texts the adapter knows about the prompt's work, which join the Redo texts of a rejection. Result holds a character other
/// than whitespace when IsError is true.</summary>
public sealed record AgentAnswer(string Result, int NumTurns, decimal CostUsd, bool IsError, IReadOnlyDictionary<string, string> Outputs, IReadOnlyList<string> Faults);

/// <summary>Runs one prompt through an agent and gives its answer. <see cref="ClaudeCodeAgent"/> runs <c>claude</c> in a
/// <see cref="Sandbox"/>; <see cref="ManagedAgentsAgent"/> sends the prompt to a Managed Agents session. Each adapter keeps the
/// outputs of the answer fresh: an output in the answer is one that this prompt wrote.</summary>
public abstract class Agent
{
    /// <summary>The result of an answer whose <c>IsError</c> is true and whose own text is blank.</summary>
    public const string NoTextError = "The agent answered with an error and no text.";

    /// <summary>Runs <paramref name="request"/> and gives its answer.</summary>
    public abstract Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="ArgumentException"/> when an output of <paramref name="request"/> is not a plain base name of
    /// letters, digits, <c>.</c>, <c>-</c> and <c>_</c>. An adapter puts each name in a path and in a shell command.</summary>
    protected static void RefuseUnsafeOutputs(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (string name in request.Outputs)
        {
            if (name is "." or ".." || name.Length is 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                throw new ArgumentException($"The output name \"{name}\" is not a plain base name.", nameof(request));
            }
        }
    }
}
