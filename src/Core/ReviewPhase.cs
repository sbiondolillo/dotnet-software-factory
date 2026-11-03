using System.Text.Json;

namespace Core;

/// <summary>The verdict of <see cref="ReviewPhase"/>: an approval, or a rejection with its blocking items.</summary>
public sealed record ReviewPhaseResult(bool Approved, IReadOnlyList<string> Blocking, bool IsError = false);

/// <summary>Asks the agent to review the build against the issue, by the steps of <paramref name="jobSteps"/>, and reads its verdict.</summary>
public sealed class ReviewPhase(Agent agent, Git git, string jobSteps)
{
    private const string VerdictName = "review.json";
    private const string VerdictPath = ".factory/" + VerdictName;

    /// <summary>Runs one review prompt and gives back the verdict the agent wrote.</summary>
    public async Task<ReviewPhaseResult> RunAsync(
        string title,
        string body,
        string model,
        int maxTurns,
        string workTree,
        string baseCommit,
        CancellationToken cancellationToken = default)
    {
        string diff = await git.ReadDiffAsync(baseCommit, workTree, cancellationToken).ConfigureAwait(false);
        AgentPrompt prompt = BuildPrompt(title, body, baseCommit, diff);

        AgentAnswer answer = await agent.RunAsync(new AgentRequest(prompt, model, maxTurns, Phase.Review, [VerdictName]), cancellationToken).ConfigureAwait(false);
        if (answer.IsError)
        {
            return new ReviewPhaseResult(false, [answer.Result], true);
        }

        return ParseVerdict(answer.Outputs.GetValueOrDefault(VerdictName));
    }

    private static ReviewPhaseResult Reject(string item) => new(false, [item]);

    /// <summary>The verdict that <paramref name="text"/> states, or a rejection that names its fault. Null text is a verdict the
    /// agent did not write.</summary>
    private static ReviewPhaseResult ParseVerdict(string? text)
    {
        string fault = $"{VerdictPath} must hold one JSON object with approved (true or false) and blocking (a list of texts)";
        if (text is null)
        {
            return Reject($"{fault}: the file is absent.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return Reject($"{fault}: the text is not one JSON object.");
        }

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            document.Dispose();
            return Reject($"{fault}: the text is not one JSON object.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("approved", out JsonElement approved)
                || approved.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Reject($"{fault}: approved is missing or not true or false.");
            }

            var blocking = new List<string>();
            if (root.TryGetProperty("blocking", out JsonElement list))
            {
                if (list.ValueKind is not JsonValueKind.Array)
                {
                    return Reject($"{fault}: blocking is not a list.");
                }

                foreach (JsonElement item in list.EnumerateArray())
                {
                    if (item.ValueKind is not JsonValueKind.String)
                    {
                        return Reject($"{fault}: blocking holds an item that is not a text.");
                    }

                    blocking.Add(item.GetString() ?? "");
                }
            }

            if (blocking.Count is 0)
            {
                return approved.ValueKind is JsonValueKind.True
                    ? new ReviewPhaseResult(true, [])
                    : Reject($"{VerdictPath} states approved false and no blocking item; state each fault as a blocking item.");
            }

            return new ReviewPhaseResult(false, blocking);
        }
    }

    private AgentPrompt BuildPrompt(string title, string body, string baseCommit, string diff) => new(
        [diff],
        pieces =>
            "Review the build of the issue below.\n\n"
            + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
            + $"Base commit: {baseCommit}\n"
            + "Read every change since the base commit in the work tree.\n\n"
            + "Your job:\n"
            + jobSteps
            + $"3. Write the verdict to {VerdictPath}, as one JSON object with approved and blocking. "
            + "approved is true or false; blocking is a list of texts. Approve only a build with no blocking item. "
            + "Write each blocking item as one fault, in words that a builder can act on with the item alone.\n"
            + "4. Leave every other file as it is.\n\n"
            + $"The diff of the work tree against the base commit:\n{pieces[0]}");
}
