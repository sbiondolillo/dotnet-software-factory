namespace Core;

/// <summary>What <see cref="Run.RunAsync"/> reads of a request, which both run kinds share: the repo, the clone, the sandbox, the
/// review model, the author of each commit, the names of the secrets and the agents' settings.</summary>
public sealed record RunRequest(
    string Owner,
    string Repo,
    string Token,
    string Directory,
    string ImageTag,
    string RootDirectory,
    string ReviewModel,
    int ReviewTurns,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<string> SecretNames,
    bool SkipIntegration,
    ManagedAgentsSettings? Managed);

/// <summary>What the step before the gates gives: the <paramref name="Pin"/> of the gates, or, when it is null, the text of the
/// comment that stops the run in <paramref name="Stop"/>.</summary>
public sealed record PinOrStop(Pin? Pin, string? Stop);

/// <summary>The end of a <see cref="Run"/>: what the step after a pass gave, or null on a stop, and the Redos spent.</summary>
public sealed record RunOutcome<TOutcome>(TOutcome? Passed, int Redos)
    where TOutcome : class;

/// <summary>
/// What one kind of run supplies to <see cref="Run"/>: the factory run or the feedback run. The kind keeps its own reading before
/// the clone and its own ending after a pass. <typeparamref name="TOutcome"/> is what its ending gives.
/// </summary>
public interface IRunKind<TOutcome>
    where TOutcome : class
{
    /// <summary>The kind, which picks the words of the build and the review.</summary>
    RunKind Kind { get; }

    /// <summary>The recipe of the run, with its scope and its check.</summary>
    RecipeChoice Choice { get; }

    /// <summary>The title of each prompt and the message of each commit.</summary>
    string Title { get; }

    /// <summary>The body of each prompt.</summary>
    string Body { get; }

    /// <summary>The branch of the Managed Agents sessions.</summary>
    string ScratchBranch { get; }

    /// <summary>The number of the issue or the pull request that each comment of the run goes on.</summary>
    int CommentNumber { get; }

    /// <summary>The heading of the run's section in the job summary.</summary>
    string SummaryHeading { get; }

    /// <summary>Checks out the tree the run builds on, in the clone, and gives its commit, the base of the build. Null stops the run
    /// before the agents start: the kind posted its comment.</summary>
    Task<string?> CheckoutAsync(Git git, CancellationToken cancellationToken = default);

    /// <summary>Runs between the start of the agents and the gates, and gives the pin of the gates or the text of a stop.</summary>
    Task<PinOrStop> BeforeGatesAsync(RunAgents agents, JobSummary summary, CancellationToken cancellationToken = default);

    /// <summary>The build of the build loop, over the <paramref name="builder"/>.</summary>
    IBuild Build(Agent builder);

    /// <summary>Ends a run whose build loop passed, within the life of the <paramref name="agents"/>. <paramref name="spend"/> is
    /// the cost in dollars of every answer of the run.</summary>
    Task<TOutcome> AfterPassAsync(RunAgents agents, GateSet gates, BuildLoopResult result, decimal spend, CancellationToken cancellationToken = default);
}

/// <summary>
/// The wiring that every kind of run shares, from the clone to the end of the build loop: the agents, the gates, the build loop and
/// the review, the comment that a stop posts, and the run's section of the job summary, appended to the file at
/// <paramref name="summaryPath"/> when the run wrote a row. A write that fails goes to <paramref name="log"/>. The
/// <paramref name="handler"/> and the <paramref name="delay"/> serve the Managed Agents path, when the request holds its settings.
/// </summary>
public sealed class Run(
    CommandRunner runner,
    GitHubClient client,
    TextWriter? progress = null,
    TextWriter? log = null,
    TimeProvider? time = null,
    string? summaryPath = null,
    HttpMessageHandler? handler = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider _clock = time ?? TimeProvider.System;

    /// <summary>
    /// Clones the repo, checks out the kind's tree, starts the agents, runs the kind's step before the gates, builds the gates, and
    /// runs the build loop with the kind's build and the review. A stop posts its comment on the kind's issue or pull request, and
    /// gives no outcome. A <see cref="SessionBranchException"/> is such a stop. A pass gives what the kind's ending gives.
    /// </summary>
    public async Task<RunOutcome<TOutcome>> RunAsync<TOutcome>(RunRequest request, IRunKind<TOutcome> kind, CancellationToken cancellationToken = default)
        where TOutcome : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(kind);
        var summary = new JobSummary(progress);
        IReadOnlyList<SessionCost> sessions = [];
        decimal spend = 0m;
        try
        {
            var git = new Git(runner);
            await Say("clone: start").ConfigureAwait(false);
            await git.CloneAsync($"https://github.com/{request.Owner}/{request.Repo}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
            if (await kind.CheckoutAsync(git, cancellationToken).ConfigureAwait(false) is not string baseCommit)
            {
                return new RunOutcome<TOutcome>(null, 0);
            }

            var setup = new RunAgentsSetup(
                runner, git, request.Owner, request.Repo, request.Token, request.Directory, request.RootDirectory, request.ImageTag,
                baseCommit, kind.ScratchBranch, kind.Title, request.AuthorName, request.AuthorEmail,
                request.Managed, handler, delay, time, progress, log,
                (elapsed, answer) =>
                {
                    spend += answer.CostUsd;
                    summary.Prompt(elapsed, answer.NumTurns, answer.CostUsd);
                });
            RunAgents agents = await RunAgents.StartAsync(setup, cancellationToken).ConfigureAwait(false);
            await using (agents.ConfigureAwait(false))
            {
                try
                {
                    PinOrStop before = await kind.BeforeGatesAsync(agents, summary, cancellationToken).ConfigureAwait(false);
                    if (before.Pin is not Pin pin)
                    {
                        await PostAsync(request, kind.CommentNumber, before.Stop ?? "", cancellationToken).ConfigureAwait(false);
                        return new RunOutcome<TOutcome>(null, 0);
                    }

                    GateSet gates = await new GateFactory(agents.Sandbox, git, request.Directory, baseCommit, request.SecretNames, progress, log, request.SkipIntegration)
                        .CreateAsync(kind.Choice, pin, cancellationToken).ConfigureAwait(false);
                    if (gates.Stop is string stop)
                    {
                        await PostAsync(request, kind.CommentNumber, stop, cancellationToken).ConfigureAwait(false);
                        return new RunOutcome<TOutcome>(null, 0);
                    }

                    var reviewPhase = new ReviewPhase(agents.Reviewer, git, RecipeRules.ReviewSteps(kind.Choice.Recipe, kind.Kind));
                    var loop = new BuildLoop(summary, _clock, progress, gates.Lint, request.Directory, kind.Title, request.AuthorName, request.AuthorEmail);
                    BuildLoopResult result = await loop.RunAsync(
                        kind.Build(agents.Builder),
                        gates.Gates,
                        token => reviewPhase.RunAsync(kind.Title, kind.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, token),
                        cancellationToken).ConfigureAwait(false);
                    if (result.End is not BuildLoopEnd.Passed)
                    {
                        await PostAsync(request, kind.CommentNumber, result.Comment ?? "", cancellationToken).ConfigureAwait(false);
                        return new RunOutcome<TOutcome>(null, result.Redos);
                    }

                    TOutcome passed = await kind.AfterPassAsync(agents, gates, result, spend, cancellationToken).ConfigureAwait(false);
                    return new RunOutcome<TOutcome>(passed, result.Redos);
                }
                catch (SessionBranchException exception)
                {
                    await PostAsync(request, kind.CommentNumber, exception.Message, cancellationToken).ConfigureAwait(false);
                    return new RunOutcome<TOutcome>(null, 0);
                }
                finally
                {
                    sessions = agents.Sessions;
                }
            }
        }
        finally
        {
            await WriteSummaryAsync(summary, sessions, kind.SummaryHeading).ConfigureAwait(false);
        }
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    /// <summary>Writes <paramref name="body"/> as a progress line and posts it unchanged on the issue or the pull request <paramref name="number"/>.</summary>
    private async Task PostAsync(RunRequest request, int number, string body, CancellationToken cancellationToken)
    {
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Owner, request.Repo, number, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSummaryAsync(JobSummary summary, IReadOnlyList<SessionCost> sessions, string heading)
    {
        if (string.IsNullOrEmpty(summaryPath) || summary.IsEmpty)
        {
            return;
        }

        summary.AddSessions(sessions);
        try
        {
            await JobSummary.AppendToFileAsync(summaryPath, summary.Render(heading)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await ProgressLine.WriteAsync(log, $"summary: cannot write {summaryPath}: {exception.Message}").ConfigureAwait(false);
        }
    }
}
