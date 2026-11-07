using System.Globalization;

namespace Core;

/// <summary>The check gate: the command under <c>## Check</c> of the issue, run under <c>sh -c</c> in a copy of the head commit.</summary>
public sealed class CheckGate(Sandbox sandbox, string command, TextWriter? log = null) : IGate
{
    /// <inheritdoc/>
    public GateName Name => GateName.Check;

    /// <summary>Gives <c>green</c> when the command exits with the code 0, and <c>red</c> with its code and both outputs otherwise.</summary>
    public async Task<GateOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        CommandResult result = await RunCheckAsync(sandbox, command, log, cancellationToken).ConfigureAwait(false);
        return result.ExitCode is 0 ? new GateOutcome("green", []) : new GateOutcome("red", [Redo(result)]);
    }

    /// <summary>Runs the check before the first phase, writes <c>check: start</c> and then
    /// <c>check: green</c> or <c>check: red</c> to <paramref name="progress"/>, and gives the text of the comment that stops the
    /// run, or null when the check fails as the run needs. A check that passes stops the run, and so does the code 126 or 127,
    /// with which the shell says that the command did not run. The comment takes its phase and its work from <paramref name="words"/>.</summary>
    public static async Task<string?> PreCheckAsync(
        Sandbox sandbox,
        string command,
        PreCheckWords words,
        TextWriter? progress,
        TextWriter? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        await ProgressLine.WriteAsync(progress, "check: start").ConfigureAwait(false);
        CommandResult result = await RunCheckAsync(sandbox, command, log, cancellationToken).ConfigureAwait(false);
        await ProgressLine.WriteAsync(progress, result.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
        if (result.ExitCode is 0)
        {
            return $"The check passes {words.When}, so there is nothing to {words.Work}.";
        }

        if (result.ExitCode is 126 or 127)
        {
            return CommentText.Fit(
                string.Create(CultureInfo.InvariantCulture, $"The check command exited with the code {result.ExitCode} {words.When}, so it did not run. Its standard error:"),
                [result.StandardError]);
        }

        return null;
    }

    /// <summary>Runs <paramref name="command"/> under <c>sh -c</c> in a copy of the head commit, and logs its exit code. The log
    /// line names the check and not the command, whose text comes from the issue.</summary>
    private static Task<CommandResult> RunCheckAsync(Sandbox sandbox, string command, TextWriter? log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        return sandbox.InCopyOfHeadAsync("check", async (copy, token) =>
        {
            CommandResult ran = await sandbox.RunAsync("sh", ["-c", command], [], copy, token).ConfigureAwait(false);
            await ProgressLine.WriteAsync(log, string.Create(CultureInfo.InvariantCulture, $"command: check exited {ran.ExitCode}")).ConfigureAwait(false);
            return ran;
        }, cancellationToken);
    }

    private static string Redo(CommandResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"The check command exited with the code {result.ExitCode}\nstandard output: {result.StandardOutput}\nstandard error: {result.StandardError}");
}
