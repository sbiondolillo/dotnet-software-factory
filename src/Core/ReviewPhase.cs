using System.Text;
using System.Text.Json;

namespace Core;

/// <summary>The verdict of <see cref="ReviewPhase"/>: an approval, or a rejection with its blocking items.</summary>
public sealed record ReviewPhaseResult(bool Approved, IReadOnlyList<string> Blocking, bool IsError = false);

/// <summary>Asks the agent to review the build against the issue, the scenarios and the plan, and reads its verdict.</summary>
public sealed class ReviewPhase(ClaudeCodeAgent agent, Git git, bool cleanup = false, bool refactor = false, bool readsPlan = true, bool reframe = false)
{
    private const string VerdictPath = ".factory/review.json";
    private const string CutMarker = "[cut]";
    private const int PromptLimit = 131071;

    /// <summary>The judgement that the review prompt of a reframe run states.</summary>
    public const string ReframeText = "Judge the build against every sentence of the issue: it changes only the paths under ## Scope, keeps the behaviour of the program and makes the command under ## Check pass, and it keeps every test and every public type and member of the code under src/.";

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
        string prompt = BuildPrompt(title, body, baseCommit, diff);

        string verdictFile = Path.Combine(workTree, ".factory", "review.json");
        if (File.Exists(verdictFile))
        {
            File.Delete(verdictFile);
        }

        ClaudeCodeResult answer = await agent.RunAsync(prompt, model, maxTurns, "review", cancellationToken).ConfigureAwait(false);
        if (answer.IsError)
        {
            return new ReviewPhaseResult(false, [string.IsNullOrWhiteSpace(answer.Result) ? "claude answered with an error and no text." : answer.Result], true);
        }

        return ReadVerdict(verdictFile);
    }

    private static ReviewPhaseResult Reject(string item) => new(false, [item]);

    private static ReviewPhaseResult ReadVerdict(string path)
    {
        string fault = $"{VerdictPath} must hold one JSON object with approved (true or false) and blocking (a list of texts)";
        JsonDocument document;
        try
        {
            document = ClaudeCodeAgent.ReadJsonFile(path);
        }
        catch (ClaudeCodeOutputException exception)
        {
            return Reject($"{fault}: {exception.Message}");
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

    private string BuildPrompt(string title, string body, string baseCommit, string diff)
    {
        string Make(string shownDiff) =>
            "Review the build of the issue below.\n\n"
            + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
            + $"Base commit: {baseCommit}\n"
            + "Read every change since the base commit in the work tree.\n\n"
            + "Your job:\n"
            + (reframe
                ? "1. Read every feature file under specs/features/.\n"
                    + "2. " + ReframeText + "\n"
                : refactor
                ? (readsPlan ? "1. Read the plan, .factory/plan.md.\n" : "1. Read every change since the base commit.\n")
                    + (readsPlan ? "2. Judge the build against the plan and every sentence of the issue. " : "2. Judge the build against every sentence of the issue. ")
                    + "Block a change to any file under specs/features/, tests/Specs/ or tests/Integration/.\n"
                : cleanup
                ? "1. Read every feature file under specs/features/.\n"
                    + "2. Judge the build against every sentence of the issue: it changes only the paths under ## Scope and makes the command under ## Check pass, "
                    + "and it keeps every test.\n"
                : "1. Read every feature file under specs/features/ and the plan, .factory/plan.md.\n"
                    + "2. Judge the build against every scenario, every result the plan states and every sentence of the issue, "
                    + "and judge whether the tests cover what each scenario names. "
                    + "For each canned answer that a scenario depends on, block a build with no test under tests/Integration/ that gets the same answer from the real service. "
                    + "An existing test is a scenario under specs/features/, or a [Fact] or [Theory] method under tests/Specs/Unit/ or tests/Integration/, at the base commit. "
                    + "Each row of the Examples of a Scenario Outline, and each [InlineData] row of a [Theory], is a test, and an edit to the row is an edit to that test. "
                    + "A step class, a helper and a stub are not tests. "
                    + "Block an edit to the text of an existing scenario or to the body of an existing test method, a rename or a removal, when the issue does not name that test. "
                    + "A wrapper around the body, such as a retry, is an edit. "
                    + "An edit to a step that an existing scenario uses keeps what the step does and what it asserts. "
                    + "New steps, state of a step class moved into a shared class, and new helpers pass.\n")
            + $"3. Write the verdict to {VerdictPath}, as one JSON object with approved and blocking. "
            + "approved is true or false; blocking is a list of texts. Approve only a build with no blocking item. "
            + "Write each blocking item as one fault, in words that a builder can act on with the item alone.\n"
            + "4. Leave every other file as it is.\n\n"
            + $"The diff of the work tree against the base commit:\n{shownDiff}";

        string whole = Make(diff);
        if (Encoding.UTF8.GetByteCount(whole) <= PromptLimit)
        {
            return whole;
        }

        int low = 0;
        int high = diff.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Encoding.UTF8.GetByteCount(Make(Prefix(diff, middle) + CutMarker)) <= PromptLimit)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Make(Prefix(diff, low) + CutMarker);
    }

    private static string Prefix(string text, int length) =>
        length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];
}
