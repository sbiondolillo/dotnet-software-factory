using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Core;

/// <summary>Runs one prompt through <c>claude</c> in a <see cref="Sandbox"/> and reads JSON files the agent wrote.</summary>
public sealed class ClaudeCodeAgent(Sandbox sandbox)
{
    private const int PromptLimit = 131072;

    /// <summary>Runs <paramref name="prompt"/> with <paramref name="model"/> and <paramref name="maxTurns"/>.</summary>
    public async Task<ClaudeCodeResult> RunAsync(
        string prompt,
        string model,
        int maxTurns,
        CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(prompt) >= PromptLimit)
        {
            throw new ClaudeCodeException($"The prompt holds {PromptLimit} bytes or more in UTF-8, the limit of one argument.");
        }

        CommandResult result = await sandbox.RunAsync(
            "claude",
            [
                "-p", prompt,
                "--model", model,
                "--max-turns", maxTurns.ToString(CultureInfo.InvariantCulture),
                "--output-format", "json",
                "--dangerously-skip-permissions",
            ],
            [],
            cancellationToken).ConfigureAwait(false);

        using JsonDocument? document = ParseObject(result.StandardOutput);
        if (document is null)
        {
            throw new ClaudeCodeException(
                $"claude exited with the code {result.ExitCode} and wrote no JSON object: {result.StandardError}");
        }

        JsonElement root = document.RootElement;
        return new ClaudeCodeResult(
            root.TryGetProperty("result", out JsonElement text) && text.ValueKind is JsonValueKind.String ? text.GetString() ?? "" : "",
            root.TryGetProperty("num_turns", out JsonElement turns) && turns.ValueKind is JsonValueKind.Number ? turns.GetInt32() : 0,
            root.TryGetProperty("total_cost_usd", out JsonElement cost) && cost.ValueKind is JsonValueKind.Number ? cost.GetDecimal() : 0m,
            root.TryGetProperty("is_error", out JsonElement error) && error.ValueKind is JsonValueKind.True);
    }

    /// <summary>Reads the JSON object in the file at <paramref name="path"/>.</summary>
    public static JsonDocument ReadJsonFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ClaudeCodeOutputException(path, exception.Message);
        }

        return ParseObject(text) ?? throw new ClaudeCodeOutputException(path, "the text is not one JSON object");
    }

    private static JsonDocument? ParseObject(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind is JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();
        return null;
    }
}
