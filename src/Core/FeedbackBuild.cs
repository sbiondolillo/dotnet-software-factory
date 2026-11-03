using System.Text.Json;

namespace Core;

/// <summary>The answer of a feedback build to one review thread: the id of the thread and the reply for the reviewer.</summary>
public sealed record ThreadAnswer(string Id, string Reply);

/// <summary>
/// The build of a feedback round: one prompt of <paramref name="phase"/> over the round's <paramref name="title"/> and
/// <paramref name="body"/>, which asks for the output <see cref="ReportName"/>, then a parse of its text. An answer with
/// <c>is_error</c> is the stop, and the report is not read. Each fault of the report is a Redo text, and a report with no fault
/// gives one <see cref="ThreadAnswer"/> for each of <paramref name="threads"/>. The faults of the answer pass through.
/// </summary>
public sealed class FeedbackBuild(
    BuildPhase phase,
    string title,
    string body,
    string model,
    int maxTurns,
    IReadOnlyList<ReviewThread> threads) : IBuild
{
    /// <summary>The base name of the report.</summary>
    public const string ReportName = "threads.json";

    /// <summary>The path of the report under the work tree.</summary>
    public const string ReportPath = ".factory/" + ReportName;

    /// <summary>The answers of the last build whose report has no fault, or none.</summary>
    public IReadOnlyList<ThreadAnswer> Answers { get; private set; } = [];

    /// <inheritdoc/>
    public async Task<BuildOutcome> RunAsync(IReadOnlyList<string> redoTexts, CancellationToken cancellationToken = default)
    {
        Answers = [];
        BuildPhaseResult built = await phase.RunAsync(title, body, model, maxTurns, redoTexts, [ReportName], cancellationToken).ConfigureAwait(false);
        if (!built.Passed)
        {
            return new BuildOutcome(built.Failure, [], built.Faults);
        }

        (IReadOnlyList<ThreadAnswer> answers, IReadOnlyList<string> faults) = ParseReport(built.Outputs.GetValueOrDefault(ReportName), threads);
        if (faults.Count is 0)
        {
            Answers = answers;
        }

        return new BuildOutcome(null, faults, built.Faults);
    }

    /// <summary>The answers and the faults of the report <paramref name="text"/>. Null text is a report the agent did not write.</summary>
    private static (IReadOnlyList<ThreadAnswer> Answers, IReadOnlyList<string> Faults) ParseReport(string? text, IReadOnlyList<ReviewThread> threads)
    {
        if (text is null)
        {
            return ([], [$"The report {ReportPath} is absent. Write it."]);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(text);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ([], [$"The report {ReportPath} is not JSON. Write one JSON object with a threads list."]);
        }

        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("threads", out JsonElement list) || list.ValueKind is not JsonValueKind.Array)
        {
            return ([], [$"The report {ReportPath} is not one JSON object with a threads list."]);
        }

        var objects = list.EnumerateArray().Where(item => item.ValueKind is JsonValueKind.Object).ToList();
        var answers = new List<ThreadAnswer>();
        var faults = new List<string>();
        foreach (ReviewThread thread in threads)
        {
            JsonElement? found = objects.Cast<JsonElement?>().FirstOrDefault(item => item!.Value.TryGetProperty("id", out JsonElement id) && id.ValueKind is JsonValueKind.String && id.GetString() == thread.Id);
            if (found is not JsonElement element)
            {
                faults.Add($"The report {ReportPath} has no item for the thread {thread.Id}.");
                continue;
            }

            string? status = element.TryGetProperty("status", out JsonElement s) && s.ValueKind is JsonValueKind.String ? s.GetString() : null;
            bool statusOk = status is "fixed" or "disputed";
            if (!statusOk)
            {
                string shown = element.TryGetProperty("status", out JsonElement raw) ? raw.ToString() : "missing";
                faults.Add($"The item of the thread {thread.Id} has the status {shown}. The status is fixed or disputed.");
            }

            string? reply = element.TryGetProperty("reply", out JsonElement r) && r.ValueKind is JsonValueKind.String ? r.GetString() : null;
            bool replyOk = !string.IsNullOrWhiteSpace(reply);
            if (!replyOk)
            {
                faults.Add($"The item of the thread {thread.Id} has no reply. The reply holds a character other than whitespace.");
            }

            if (statusOk && replyOk)
            {
                answers.Add(new ThreadAnswer(thread.Id, reply!));
            }
        }

        return (answers, faults);
    }
}
