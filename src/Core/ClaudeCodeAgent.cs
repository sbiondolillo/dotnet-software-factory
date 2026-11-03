using System.Globalization;
using System.Text.Json;

namespace Core;

/// <summary>Runs one prompt through <c>claude</c> in a <see cref="Sandbox"/> over <paramref name="workTree"/>. It deletes each
/// output the request names under <c>.factory/</c> of the work tree before the prompt, and reads each one after it.</summary>
public sealed class ClaudeCodeAgent(
    Sandbox sandbox,
    string workTree,
    TextWriter? log = null,
    TimeProvider? time = null,
    Action<TimeSpan, AgentAnswer>? onAnswer = null) : Agent
{
    private const int PromptLimit = 131071;

    /// <inheritdoc />
    public override async Task<AgentAnswer> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        RefuseUnsafeOutputs(request);
        string prompt = request.Prompt.Fit(PromptLimit) ?? throw new ClaudeCodeException(
            $"The issue and the job hold more than {PromptLimit} bytes in UTF-8, and no Redo text can be cut to make room.");

        foreach (string name in request.Outputs)
        {
            RefuseLink(name);
            string path = OutputPath(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        string phase = request.Phase.Text();
        TimeProvider clock = time ?? TimeProvider.System;
        await ProgressLine.WriteAsync(log, $"agent: {phase} start").ConfigureAwait(false);
        long started = clock.GetTimestamp();
        CommandResult result = await sandbox.RunAsync(
            "claude",
            [
                "-p", prompt,
                "--model", request.Model,
                "--max-turns", request.MaxTurns.ToString(CultureInfo.InvariantCulture),
                "--output-format", "json",
                "--dangerously-skip-permissions",
            ],
            [],
            cancellationToken).ConfigureAwait(false);

        using JsonDocument document = ParseObject(result.StandardOutput) ?? throw new ClaudeCodeException(
            $"claude exited with the code {result.ExitCode} and wrote no JSON object: {result.StandardError}");

        JsonElement root = document.RootElement;
        string text = root.TryGetProperty("result", out JsonElement value) && value.ValueKind is JsonValueKind.String ? value.GetString() ?? "" : "";
        bool isError = root.TryGetProperty("is_error", out JsonElement error) && error.ValueKind is JsonValueKind.True;
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in request.Outputs)
        {
            string path = OutputPath(name);
            if (File.Exists(path))
            {
                RefuseLink(name);
                string output = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                RefuseLink(name);
                outputs[name] = output;
            }
        }

        var answer = new AgentAnswer(
            isError && string.IsNullOrWhiteSpace(text) ? NoTextError : text,
            root.TryGetProperty("num_turns", out JsonElement turns) && turns.ValueKind is JsonValueKind.Number ? turns.GetInt32() : 0,
            root.TryGetProperty("total_cost_usd", out JsonElement cost) && cost.ValueKind is JsonValueKind.Number ? cost.GetDecimal() : 0m,
            isError,
            outputs,
            []);
        TimeSpan elapsed = clock.GetElapsedTime(started);
        long seconds = (long)elapsed.TotalSeconds;
        onAnswer?.Invoke(elapsed, answer);
        await ProgressLine.WriteAsync(
            log,
            string.Create(CultureInfo.InvariantCulture, $"agent: {phase} end, {answer.NumTurns} turns, ${answer.CostUsd}, {seconds} s")).ConfigureAwait(false);
        return answer;
    }

    private string OutputPath(string name) => Path.Combine(workTree, ".factory", name);

    /// <summary>Throws when the output <paramref name="name"/> or <c>.factory</c> is a symbolic link. The container writes the
    /// work tree, and a link would put a file of the runner in the answer, and from there in a comment. The check runs before
    /// the delete that comes before the prompt, so the delete stays under the work tree, and before and after the read, so a
    /// swap during the read throws too.</summary>
    private void RefuseLink(string name)
    {
        if (new DirectoryInfo(Path.Combine(workTree, ".factory")).LinkTarget is not null || new FileInfo(OutputPath(name)).LinkTarget is not null)
        {
            throw new ClaudeCodeException($"The output {name} under .factory/ is a symbolic link, so the host does not read it.");
        }
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
