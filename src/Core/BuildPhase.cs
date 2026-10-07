using System.Text;

namespace Core;

/// <summary>The outcome of <see cref="BuildPhase"/>: passed, or failed with the text claude gave.</summary>
public sealed record BuildPhaseResult(bool Passed, string Failure);

/// <summary>Asks the agent to implement the plan and bind every pinned scenario.</summary>
public sealed class BuildPhase(ClaudeCodeAgent agent, bool cleanup = false, bool refactor = false, bool readsPlan = true)
{
    private const int PromptLimit = 131071;
    private const string CutMarker = "[cut]";

    /// <summary>Runs one build prompt built from the issue and the Redo texts.</summary>
    public async Task<BuildPhaseResult> RunAsync(
        string title,
        string body,
        string model,
        int maxTurns,
        IReadOnlyList<string> redoTexts,
        CancellationToken cancellationToken = default)
    {
        string prompt = BuildPrompt(title, body, redoTexts);
        ClaudeCodeResult answer = await agent.RunAsync(prompt, model, maxTurns, "build", cancellationToken).ConfigureAwait(false);
        return answer.IsError ? new BuildPhaseResult(false, answer.Result) : new BuildPhaseResult(true, "");
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
        if (cleanup)
        {
            prompt.Append("Do the cleanup of the issue below.\n\n")
                .Append("Issue title:\n").Append(title).Append("\n\n")
                .Append("Issue body:\n").Append(body).Append("\n\n")
                .Append("Change only the paths that the issue names under the heading ## Scope. ")
                .Append("Make the command in the first fenced block under the heading ## Check exit with the code 0, and keep every test that passes now passing. ")
                .Append("Leave each feature file under specs/features/ as it is. Run dotnet build and dotnet test, and work until both pass.");
            AppendRedos(prompt, redoTexts);
            return prompt.ToString();
        }

        if (refactor)
        {
            prompt.Append("Do the refactor of the issue below.\n\n")
                .Append("Issue title:\n").Append(title).Append("\n\n")
                .Append("Issue body:\n").Append(body).Append("\n\n")
                .Append(readsPlan
                    ? "Read the plan, .factory/plan.md, and change the structure of the code as it says. "
                    : "Change the structure of the code as the issue says. ")
                .Append("Change no file under specs/features/, tests/Specs/ or tests/Integration/: every test stays as it is. ")
                .Append("Leave each build settings file as it is; the build settings files are named ")
                .Append(string.Join(", ", Pin.SettingsFileNames.Order(StringComparer.Ordinal)))
                .Append(". Run dotnet build and dotnet test, and work until both pass.");
            AppendRedos(prompt, redoTexts);
            return prompt.ToString();
        }

        prompt.Append("Build the work of the issue below.\n\n")
            .Append("Issue title:\n").Append(title).Append("\n\n")
            .Append("Issue body:\n").Append(body).Append("\n\n")
            .Append("Read the plan, .factory/plan.md, and each feature file under specs/features/. ")
            .Append("Write the code that the plan and the scenarios ask for. ")
            .Append("Bind every scenario of every feature file, in step classes under tests/Specs/Steps/. ")
            .Append("Write unit tests for the new code under tests/Specs/Unit/. ")
            .Append("Leave each feature file and each build settings file as it is; the build settings files are named ")
            .Append(string.Join(", ", Pin.SettingsFileNames.Order(StringComparer.Ordinal)))
            .Append(". Run dotnet build and dotnet test, and work until both pass.");

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
