using System.Globalization;
using System.Text;

namespace Core;

/// <summary>What a <see cref="FeedbackRun"/> needs: the pull request, the clone, the models and the author.</summary>
public sealed record FeedbackRunRequest(
    string Owner,
    string Repo,
    int PullNumber,
    string Token,
    string Directory,
    string BuildModel,
    int BuildTurns,
    string ReviewModel,
    int ReviewTurns,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<string> SecretNames,
    bool SkipIntegration = false);

/// <summary>The outcome of a <see cref="FeedbackRun"/>: the threads resolved, the threads left unresolved, and the Redos spent.
/// The run resolves each thread that it replies to, so the threads left unresolved are the threads with no reply.</summary>
public sealed record FeedbackRunResult(int Resolved, int Unresolved, int Redos);

/// <summary>Answers every unresolved review thread of a pull request within the budget of the <see cref="BuildLoop"/>, with the
/// agents of the backend that <paramref name="startBackend"/> starts. A round that builds appends its section
/// <c>## Feedback run</c> to the file at <paramref name="summaryPath"/>, and a write that fails goes to <paramref name="log"/>.</summary>
public sealed class FeedbackRun(
    CommandRunner runner,
    GitHubClient client,
    StartBackend startBackend,
    TextWriter? progress = null,
    TimeProvider? time = null,
    string? summaryPath = null,
    TextWriter? log = null)
{
    /// <summary>The heading of the section of a round in the job summary.</summary>
    public const string SummaryHeading = "Feedback run";

    private readonly Run _run = new(runner, client, startBackend, progress, log, time, summaryPath);

    /// <summary>True when the last <see cref="RunAsync"/> ended on a closing issue that the recipe rules decline, with a
    /// comment on the pull request and no build.</summary>
    public bool Declined { get; private set; }

    /// <summary>
    /// Builds an answer to each unresolved thread, gates and reviews it, pushes it, and replies to each thread. Then it
    /// resolves each thread that it replied to, fixed or disputed. The reply of a disputed thread states the dispute. A round
    /// that stops on an error or spends its budget posts the comment of the loop on the pull request and pushes nothing. A
    /// <see cref="SessionBranchException"/> posts its message on the pull request and stops the round in the same way.
    /// </summary>
    public async Task<FeedbackRunResult> RunAsync(FeedbackRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Declined = false;
        var reviewThreads = new ReviewThreads(client);
        IReadOnlyList<ReviewThread> all = await reviewThreads.ReadThreadsAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false);
        List<ReviewThread> threads = [.. all.Where(thread => !thread.IsResolved)];
        if (threads.Count is 0)
        {
            return new FeedbackRunResult(0, 0, 0);
        }

        string branch = await new PullRequests(client).ReadHeadBranchAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false);
        var pullRequests = new PullRequests(client);
        // With no closing issue, the round takes the choice of an issue with no label.
        RecipeChoice choice = RecipeRules.Parse([], "");
        string? issueBody = null;
        if (PullRequests.ParseClosingIssue(await pullRequests.ReadBodyAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false)) is int issueNumber)
        {
            GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, issueNumber, cancellationToken).ConfigureAwait(false);
            issueBody = issue.Body;
            choice = RecipeRules.Parse(issue.Labels, issue.Body);
            if (choice.Declined is string refusal)
            {
                await ProgressLine.WriteAsync(progress, refusal).ConfigureAwait(false);
                await client.PostCommentAsync(request.Owner, request.Repo, request.PullNumber, refusal, cancellationToken).ConfigureAwait(false);
                Declined = true;
                return new FeedbackRunResult(0, threads.Count, 0);
            }
        }

        string title = $"Answer the review threads of pull request #{request.PullNumber.ToString(CultureInfo.InvariantCulture)}";
        var kind = new FeedbackKind(request, reviewThreads, threads, branch, choice, title, Describe(threads, issueBody, RecipeRules.ThreadRules(choice.Recipe)));
        var shared = new RunRequest(
            request.Owner, request.Repo, request.Token, request.Directory, request.ReviewModel, request.ReviewTurns, request.AuthorName,
            request.AuthorEmail, request.SecretNames, request.SkipIntegration);
        RunOutcome<FeedbackRunResult> outcome = await _run.RunAsync(shared, kind, cancellationToken).ConfigureAwait(false);
        return outcome.Passed ?? new FeedbackRunResult(0, threads.Count, outcome.Redos);
    }

    /// <summary>
    /// Gives the body of the builder and the reviewer. With no closing issue, it is the threads and the report instructions.
    /// With a closing issue, the issue body comes first, then a <c>## Review threads</c> section, then the report instructions.
    /// The <paramref name="threadRules"/> of the recipe end the report instructions, when they are not empty.
    /// </summary>
    private static string Describe(IReadOnlyList<ReviewThread> threads, string? issueBody, string threadRules)
    {
        var text = new StringBuilder();
        if (issueBody is null)
        {
            text.Append("Answer each review thread below.\n");
        }
        else
        {
            text.Append(issueBody);
            if (!issueBody.EndsWith('\n'))
            {
                text.Append('\n');
            }

            text.Append("\n## Review threads\n\n")
                .Append("The work tree holds the build of the issue above. The threads below are the work. ")
                .Append("Change the work tree only where a thread asks for it.\n");
        }

        foreach (ReviewThread thread in threads)
        {
            text.Append("\nThread ").Append(thread.Id).Append('\n').Append("Path: ").Append(thread.Path).Append('\n');
            if (thread.Line is int line)
            {
                text.Append("Line: ").Append(line.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            foreach (ReviewThreadComment comment in thread.Comments)
            {
                text.Append("Comment by ").Append(comment.Author).Append(":\n").Append(comment.Body).Append('\n');
            }
        }

        text.Append("\nWhen the work is done, write the report ").Append(FeedbackBuild.ReportPath)
            .Append(", one JSON object with the key threads: a list with one item for each thread above. ")
            .Append("Each item has an id (the thread id), a status and a reply. ")
            .Append("The status fixed means that you changed the work tree to answer the thread. ")
            .Append("The status disputed means that you leave the work tree as it is on that point. ")
            .Append("The reply is the text for the reviewer.");
        if (threadRules.Length > 0)
        {
            text.Append(' ').Append(threadRules);
        }

        return text.ToString();
    }

    /// <summary>
    /// The feedback round as a kind of <see cref="Run"/>. The checkout takes the head branch of the pull request, and the step before
    /// the gates pins the work tree as the recipe says. A pass puts the session's commits on the head branch, pushes it, and replies
    /// to each thread and resolves it.
    /// </summary>
    private sealed class FeedbackKind(
        FeedbackRunRequest request,
        ReviewThreads reviewThreads,
        IReadOnlyList<ReviewThread> threads,
        string branch,
        RecipeChoice choice,
        string title,
        string body) : IRunKind<FeedbackRunResult>
    {
        private Git? _git;
        private FeedbackBuild? _build;

        public RunKind Kind => RunKind.Feedback;

        public RecipeChoice Choice => choice;

        public string Title => title;

        public string Body => body;

        public string ScratchBranch => $"{branch}-session";

        public int CommentNumber => request.PullNumber;

        public string SummaryHeading => FeedbackRun.SummaryHeading;

        public async Task<string?> CheckoutAsync(Git git, CancellationToken cancellationToken = default)
        {
            _git = git;
            await git.CheckoutExistingBranchAsync(branch, request.Directory, cancellationToken).ConfigureAwait(false);
            return await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false);
        }

        public Task<PinOrStop> BeforeGatesAsync(AgentBackend backend, JobSummary summary, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PinOrStop(RecipeRules.TakePin(RecipeRules.Sequence(choice, RunKind.Feedback).Pin, request.Directory), null));

        public IBuild Build(Agent builder) => _build = new FeedbackBuild(
            new BuildPhase(builder, RecipeRules.BuildText(choice.Recipe, RunKind.Feedback)), title, body, request.BuildModel, request.BuildTurns, threads);

        public async Task<FeedbackRunResult> AfterPassAsync(AgentBackend backend, GateSet gates, BuildLoopResult result, decimal spend, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(result);
            Git git = _git ?? throw new InvalidOperationException("The round pushes only after its checkout.");
            FeedbackBuild build = _build ?? throw new InvalidOperationException("The round replies only after its build.");
            await backend.CollectAsync(branch, cancellationToken).ConfigureAwait(false);
            await git.PushAsync(branch, request.Directory, request.Token, cancellationToken).ConfigureAwait(false);
            int resolved = 0;
            foreach (ReviewThread thread in threads)
            {
                ThreadAnswer answer = build.Answers.First(a => a.Id == thread.Id);
                await reviewThreads.ReplyAsync(thread.Id, answer.Reply, cancellationToken).ConfigureAwait(false);
                await reviewThreads.ResolveAsync(thread.Id, cancellationToken).ConfigureAwait(false);
                resolved++;
            }

            return new FeedbackRunResult(resolved, threads.Count - resolved, result.Redos);
        }
    }
}
