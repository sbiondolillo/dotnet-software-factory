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
        CommandResult result = await CleanupGate.RunCheckAsync(sandbox, command, null, cancellationToken, log).ConfigureAwait(false);
        return result.ExitCode is 0 ? new GateOutcome("green", []) : new GateOutcome("red", [CleanupGate.CheckRedo(result)]);
    }

    /// <summary>Runs the check before the first phase of <paramref name="recipe"/>, writes <c>check: start</c> and then
    /// <c>check: green</c> or <c>check: red</c> to <paramref name="progress"/>, and gives the text of the comment that stops the
    /// run, or null when the check fails as the run needs. A check that passes stops the run, and so does the code 126 or 127,
    /// with which the shell says that the command did not run.</summary>
    public static async Task<string?> PreCheckAsync(
        Sandbox sandbox,
        string command,
        Recipe recipe,
        TextWriter? progress,
        TextWriter? log = null,
        CancellationToken cancellationToken = default)
    {
        CommandResult result = await CleanupGate.RunCheckAsync(sandbox, command, progress, cancellationToken, log).ConfigureAwait(false);
        await ProgressLine.WriteAsync(progress, result.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
        PreCheckWords words = RecipeRules.PreCheck(recipe);
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
}
