using System.Text;

namespace Core;

/// <summary>The outcome of <see cref="BuildPhase"/>: passed, or failed with the text the agent gave. <paramref name="Outputs"/>
/// holds the text of each output the agent wrote, and <paramref name="Faults"/> the faults the agent knows about the build.</summary>
public sealed record BuildPhaseResult(bool Passed, string Failure, IReadOnlyDictionary<string, string> Outputs, IReadOnlyList<string> Faults);

/// <summary>Asks the agent for the work that <paramref name="text"/> states, around the issue and the Redo texts.</summary>
public sealed class BuildPhase(Agent agent, BuildText text)
{
    /// <summary>Runs one build prompt built from the issue and the Redo texts, and asks for the base names of
    /// <paramref name="outputs"/>.</summary>
    public async Task<BuildPhaseResult> RunAsync(
        string title,
        string body,
        string model,
        int maxTurns,
        IReadOnlyList<string> redoTexts,
        IReadOnlyList<string> outputs,
        CancellationToken cancellationToken = default)
    {
        var prompt = new AgentPrompt(redoTexts, pieces => Compose(title, body, pieces));
        AgentAnswer answer = await agent.RunAsync(new AgentRequest(prompt, model, maxTurns, Phase.Build, outputs), cancellationToken).ConfigureAwait(false);
        return new BuildPhaseResult(!answer.IsError, answer.IsError ? answer.Result : "", answer.Outputs, answer.Faults);
    }

    private string Compose(string title, string body, IReadOnlyList<string> redoTexts)
    {
        var prompt = new StringBuilder();
        prompt.Append(text.Lead).Append("\n\n")
            .Append("Issue title:\n").Append(title).Append("\n\n")
            .Append("Issue body:\n").Append(body).Append("\n\n")
            .Append(text.Instructions);
        AppendRedos(prompt, redoTexts);
        return prompt.ToString();
    }

    private static void AppendRedos(StringBuilder prompt, IReadOnlyList<string> redoTexts)
    {
        if (redoTexts.Count > 0)
        {
            prompt.Append("\n\nThe work tree holds the earlier build. Each text below is a fault to correct in that build.\n");
            foreach (string text in redoTexts)
            {
                prompt.Append("\nRedo text:\n").Append(text).Append('\n');
            }
        }
    }
}
