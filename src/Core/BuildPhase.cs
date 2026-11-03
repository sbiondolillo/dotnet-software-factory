using System.Text;

namespace Core;

/// <summary>The outcome of <see cref="BuildPhase"/>: passed, or failed with the text the agent gave. <paramref name="Outputs"/>
/// holds the text of each output the agent wrote, and <paramref name="Faults"/> the faults the agent knows about the build.</summary>
public sealed record BuildPhaseResult(bool Passed, string Failure, IReadOnlyDictionary<string, string> Outputs, IReadOnlyList<string> Faults);

/// <summary>Asks the agent for the work that <paramref name="text"/> states, around the issue and the Redo texts.</summary>
public sealed class BuildPhase(Agent agent, BuildText text)
{
    private const int PromptLimit = 131071;
    private const string CutMarker = "[cut]";

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
        string prompt = BuildPrompt(title, body, redoTexts);
        AgentAnswer answer = await agent.RunAsync(new AgentRequest(prompt, model, maxTurns, Phase.Build, outputs), cancellationToken).ConfigureAwait(false);
        return new BuildPhaseResult(!answer.IsError, answer.IsError ? answer.Result : "", answer.Outputs, answer.Faults);
    }

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private string BuildPrompt(string title, string body, IReadOnlyList<string> redoTexts)
    {
        string whole = Compose(title, body, redoTexts);
        if (Bytes(whole) <= PromptLimit)
        {
            return whole;
        }

        int low = 0;
        int high = redoTexts.Count is 0 ? 0 : redoTexts.Max(Bytes);
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (Bytes(Compose(title, body, [.. redoTexts.Select(text => Cut(text, middle))])) <= PromptLimit)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        string result = Compose(title, body, [.. redoTexts.Select(text => Cut(text, low))]);
        if (Bytes(result) > PromptLimit)
        {
            throw new ClaudeCodeException($"The issue and the job hold more than {PromptLimit} bytes in UTF-8, and no Redo text can be cut to make room.");
        }

        return result;
    }

    private static string Cut(string text, int limit)
    {
        if (Bytes(text) <= limit)
        {
            return text;
        }

        int budget = limit - Bytes(CutMarker);
        var builder = new StringBuilder();
        int used = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            used += rune.Utf8SequenceLength;
            if (used > budget)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.Append(CutMarker).ToString();
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
