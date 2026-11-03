namespace Core;

/// <summary>What one build gave: <paramref name="Stop"/> is the text of an error that stops the run, or null when the build
/// passed, and <paramref name="RedoTexts"/> are the faults of the build itself, which give a Redo after the build row.
/// <paramref name="Faults"/> are the faults the agent knows about the build, which join the Redo texts of a rejection.</summary>
public sealed record BuildOutcome(string? Stop, IReadOnlyList<string> RedoTexts, IReadOnlyList<string> Faults);

/// <summary>One build of a run, given the Redo texts of the last rejection, or none before the first build.</summary>
public interface IBuild
{
    /// <summary>Runs the build with <paramref name="redoTexts"/>.</summary>
    Task<BuildOutcome> RunAsync(IReadOnlyList<string> redoTexts, CancellationToken cancellationToken = default);
}

/// <summary>How a <see cref="BuildLoop"/> ended.</summary>
public enum BuildLoopEnd
{
    /// <summary>The review approved the build.</summary>
    Passed,

    /// <summary>The build or the review answered with an error.</summary>
    Stopped,

    /// <summary>The last build was rejected with the budget of Redos spent.</summary>
    Spent,
}

/// <summary>The outcome of a <see cref="BuildLoop"/>: how it ended, the Redos spent, and for <see cref="BuildLoopEnd.Stopped"/>
/// and <see cref="BuildLoopEnd.Spent"/> the finished body of the comment that the run posts unchanged.</summary>
public sealed record BuildLoopResult(BuildLoopEnd End, int Redos, string? Comment = null);

/// <summary>
/// The build loop of a run: the build, the lint, the gates in the order of the recipe, then the review, repeated within the
/// budget of <see cref="RedoBudget"/> Redos. It writes every row of <paramref name="summary"/> and every progress line of the
/// loop. The lint commits a change of <paramref name="workTree"/> with <paramref name="message"/> and the author.
/// The faults of a rejected build join its Redo texts, after the gates and the review.
/// A token that is cancelled when an await returns throws <see cref="OperationCanceledException"/> before the next call.
/// The loop catches nothing: an exception of a phase, the lint or a gate reaches the caller unchanged, and the rows so far stay.
/// </summary>
public sealed class BuildLoop(
    JobSummary summary,
    TimeProvider clock,
    TextWriter? progress,
    LintGate lint,
    string workTree,
    string message,
    string authorName,
    string authorEmail)
{
    /// <summary>The Redos a run can spend.</summary>
    public const int RedoBudget = 3;

    /// <summary>Runs <paramref name="build"/>, the lint, each of <paramref name="gates"/> in order until one gives Redo texts,
    /// and <paramref name="review"/> after every gate passes, until the review approves, an error stops the loop or the
    /// budget is spent.</summary>
    public async Task<BuildLoopResult> RunAsync(
        IBuild build,
        IReadOnlyList<IGate> gates,
        Func<CancellationToken, Task<ReviewPhaseResult>> review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(review);

        cancellationToken.ThrowIfCancellationRequested();
        int redos = 0;
        IReadOnlyList<string> redoTexts = [];
        while (true)
        {
            await SayAsync("build: start", cancellationToken).ConfigureAwait(false);
            long buildStart = clock.GetTimestamp();
            BuildOutcome built = await build.RunAsync(redoTexts, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (built.Stop is string stop)
            {
                summary.AddRow(Phase.Build.Text(), clock.GetElapsedTime(buildStart), "failed");
                return Stopped(redos, stop);
            }

            await SayAsync("lint: start", cancellationToken).ConfigureAwait(false);
            await lint.RunAsync(workTree, message, authorName, authorEmail, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string producer = summary.AddRow(Phase.Build.Text(), clock.GetElapsedTime(buildStart), "passed");
            redoTexts = built.RedoTexts;
            foreach (IGate gate in gates)
            {
                if (redoTexts.Count > 0)
                {
                    break;
                }

                (redoTexts, producer) = await RunGateAsync(gate, cancellationToken).ConfigureAwait(false);
            }

            if (redoTexts.Count is 0)
            {
                await SayAsync("review: start", cancellationToken).ConfigureAwait(false);
                ReviewPhaseResult reviewed = await review(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                string reviewRow = summary.AddRow(Phase.Review.Text(), null, reviewed.IsError ? "failed" : reviewed.Approved ? "approved" : "blocked");
                if (reviewed.IsError)
                {
                    return Stopped(redos, reviewed.Blocking.Count is 0 ? "" : reviewed.Blocking[0]);
                }

                await SayAsync(reviewed.Approved ? "review: approved" : "review: blocked", cancellationToken).ConfigureAwait(false);
                if (reviewed.Approved)
                {
                    return new BuildLoopResult(BuildLoopEnd.Passed, redos);
                }

                redoTexts = reviewed.Blocking;
                producer = reviewRow;
            }

            redoTexts = [.. redoTexts, .. built.Faults.Where(fault => !redoTexts.Contains(fault))];
            if (redos >= RedoBudget)
            {
                return new BuildLoopResult(
                    BuildLoopEnd.Spent,
                    redos,
                    CommentText.Fit($"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts));
            }

            redos++;
            summary.AddRedos(redos, producer, redoTexts);
            await SayAsync($"redo: {redos}", cancellationToken).ConfigureAwait(false);
        }
    }

    private static BuildLoopResult Stopped(int redos, string text) =>
        new(BuildLoopEnd.Stopped, redos, CommentText.Fit(text, []));

    /// <summary>Runs <paramref name="gate"/> between its two progress lines, adds its row, and gives its Redo texts and the name of the row.</summary>
    private async Task<(IReadOnlyList<string> RedoTexts, string Row)> RunGateAsync(IGate gate, CancellationToken cancellationToken)
    {
        string name = gate.Name.Text();
        await SayAsync($"{name}: start", cancellationToken).ConfigureAwait(false);
        long start = clock.GetTimestamp();
        GateOutcome outcome = await gate.RunAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await SayAsync($"{name}: {outcome.Result}", cancellationToken).ConfigureAwait(false);
        return (outcome.RedoTexts, summary.AddRow(name, clock.GetElapsedTime(start), outcome.Result));
    }

    /// <summary>Writes one progress line, then throws when the token is cancelled, so no call follows a cancel.</summary>
    private async Task SayAsync(string line, CancellationToken cancellationToken)
    {
        await ProgressLine.WriteAsync(progress, line).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
