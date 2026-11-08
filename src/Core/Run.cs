namespace Core;

/// <summary>What <see cref="Run.RunAsync"/> reads of a request, which both run kinds share: the repo, the clone, the review model,
/// the author of each commit and the names of the secrets.</summary>
public sealed record RunRequest(
    RepoName Repo,
    string Token,
    string Directory,
    string ReviewModel,
    int ReviewTurns,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<string> SecretNames,
    bool SkipIntegration);

/// <summary>What the step before the gates gives: the <paramref name="Pin"/> of the gates, or, when it is null, the text of the
/// comment that stops the run in <paramref name="Stop"/>.</summary>
public sealed record PinOrStop(Pin? Pin, string? Stop);

/// <summary>What the checkout gives: the <paramref name="BaseCommit"/> of the build, or, when it is null, the <paramref name="Stop"/> of the
/// run, whose comment the kind posted. One of the two is not null.</summary>
public sealed record BaseOrStop(string? BaseCommit, RunStop? Stop)
{
    /// <summary>The stop of the run, which is not null when <see cref="BaseCommit"/> is null.</summary>
    public RunStop? Stop { get; } = BaseCommit is null && Stop is null
        ? throw new ArgumentException("A checkout gives a base commit or a stop.", nameof(Stop))
        : Stop;
}

/// <summary>How a run with no pass ended: it declined its issue, or a gate, the loop or a session branch stopped it.</summary>
public enum RunEnd
{
    /// <summary>A gate, the loop or a session branch stopped the run.</summary>
    Stopped,

    /// <summary>The run declined its issue, as an input error of the issue's author.</summary>
    Declined,
}

/// <summary>Why a run ended with no pass: the comment it posted, and whether it declined its issue or a gate, the loop or a session branch stopped it.</summary>
public sealed record RunStop(string Comment, RunEnd End);

/// <summary>The end of a <see cref="Run"/>: what the step after a pass gave, or null on a stop; the stop, or null on a pass; and the
/// Redos spent.</summary>
public sealed record RunOutcome<TOutcome>(TOutcome? Passed, RunStop? Stop, int Redos)
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

    /// <summary>Checks out the tree the run builds on, in the clone, and gives its commit, the base of the build. A stop ends the run
    /// before the agents start: the kind posted its comment.</summary>
    Task<BaseOrStop> CheckoutAsync(Git git, CancellationToken cancellationToken = default);

    /// <summary>Runs between the start of the agents and the gates, and gives the pin of the gates or the text of a stop.</summary>
    Task<PinOrStop> BeforeGatesAsync(AgentBackend backend, JobSummary summary, CancellationToken cancellationToken = default);

    /// <summary>The build of the build loop, over the <paramref name="builder"/>.</summary>
    IBuild Build(Agent builder);

    /// <summary>Ends a run whose build loop passed, within the life of the <paramref name="backend"/> and of the work tree, so the
    /// ending reads the head it pushed. <paramref name="spend"/> is the cost in dollars of every answer of the run.</summary>
    Task<TOutcome> AfterPassAsync(AgentBackend backend, GateSet gates, BuildLoopResult result, decimal spend, CancellationToken cancellationToken = default);
}

/// <summary>
/// The wiring that every kind of run shares, from the clone to the end of the build loop: the agents, the gates, the build loop and
/// the review, the comment that a stop posts, and the run's section of the job summary, appended to the file at
/// <paramref name="summaryPath"/> when the run wrote a row. A write that fails goes to <paramref name="log"/>. The run takes its
/// agents and the sandbox of its gates from the backend that <paramref name="startBackend"/> starts after the checkout.
/// </summary>
public sealed class Run(
    CommandRunner runner,
    GitHubClient client,
    StartBackend startBackend,
    TextWriter? progress = null,
    TextWriter? log = null,
    TimeProvider? time = null,
    string? summaryPath = null)
{
    private readonly TimeProvider _clock = time ?? TimeProvider.System;

    /// <summary>
    /// Clones the repo, checks out the kind's tree, starts the backend, runs the kind's step before the gates, builds the gates, and
    /// runs the build loop with the kind's build and the review. A stop posts its comment on the kind's issue or pull request, and
    /// gives that comment as its <see cref="RunStop"/>. A <see cref="SessionBranchException"/> is such a stop. A pass gives what the
    /// kind's ending gives.
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
            await git.CloneAsync($"https://github.com/{request.Repo.Owner}/{request.Repo.Name}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
            BaseOrStop checkout = await kind.CheckoutAsync(git, cancellationToken).ConfigureAwait(false);
            if (checkout.BaseCommit is not string baseCommit)
            {
                return new RunOutcome<TOutcome>(null, checkout.Stop, 0);
            }

            var start = new BackendStart(
                git, request.Directory, baseCommit, kind.ScratchBranch, kind.Title,
                (elapsed, answer) =>
                {
                    spend += answer.CostUsd;
                    summary.Prompt(elapsed, answer.NumTurns, answer.CostUsd);
                });
            AgentBackend backend = await startBackend(start, cancellationToken).ConfigureAwait(false);
            await using (backend.ConfigureAwait(false))
            {
                try
                {
                    PinOrStop before = await kind.BeforeGatesAsync(backend, summary, cancellationToken).ConfigureAwait(false);
                    if (before.Pin is not Pin pin)
                    {
                        return new RunOutcome<TOutcome>(null, await PostAsync(request, kind.CommentNumber, before.Stop ?? "", cancellationToken).ConfigureAwait(false), 0);
                    }

                    GateSet gates = await new GateFactory(backend.Sandbox, git, request.Directory, baseCommit, request.SecretNames, progress, log, request.SkipIntegration)
                        .CreateAsync(kind.Choice, RecipeRules.Sequence(kind.Choice, kind.Kind), pin, cancellationToken).ConfigureAwait(false);
                    if (gates.Stop is string stop)
                    {
                        return new RunOutcome<TOutcome>(null, await PostAsync(request, kind.CommentNumber, stop, cancellationToken).ConfigureAwait(false), 0);
                    }

                    var reviewPhase = new ReviewPhase(backend.Reviewer, git, RecipeRules.ReviewSteps(kind.Choice.Recipe, kind.Kind));
                    var loop = new BuildLoop(summary, _clock, progress, gates.Lint, request.Directory, kind.Title, request.AuthorName, request.AuthorEmail);
                    BuildLoopResult result = await loop.RunAsync(
                        kind.Build(backend.Builder),
                        gates.Gates,
                        token => reviewPhase.RunAsync(kind.Title, kind.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, token),
                        cancellationToken).ConfigureAwait(false);
                    if (result.End is not BuildLoopEnd.Passed)
                    {
                        return new RunOutcome<TOutcome>(null, await PostAsync(request, kind.CommentNumber, result.Comment ?? "", cancellationToken).ConfigureAwait(false), result.Redos);
                    }

                    TOutcome passed = await kind.AfterPassAsync(backend, gates, result, spend, cancellationToken).ConfigureAwait(false);
                    return new RunOutcome<TOutcome>(passed, null, result.Redos);
                }
                catch (SessionBranchException exception)
                {
                    return new RunOutcome<TOutcome>(null, await PostAsync(request, kind.CommentNumber, exception.Message, cancellationToken).ConfigureAwait(false), 0);
                }
                finally
                {
                    sessions = backend.Sessions;
                }
            }
        }
        finally
        {
            await WriteSummaryAsync(summary, sessions, kind.SummaryHeading).ConfigureAwait(false);
        }
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    /// <summary>Writes <paramref name="body"/> as a progress line, posts it unchanged on the issue or the pull request
    /// <paramref name="number"/>, and gives the stop that it states.</summary>
    private async Task<RunStop> PostAsync(RunRequest request, int number, string body, CancellationToken cancellationToken)
    {
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Repo.Owner, request.Repo.Name, number, body, cancellationToken).ConfigureAwait(false);
        return new RunStop(body, RunEnd.Stopped);
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
