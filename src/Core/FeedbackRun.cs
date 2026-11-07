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
    string ImageTag,
    string RootDirectory,
    string BuildModel,
    int BuildTurns,
    string ReviewModel,
    int ReviewTurns,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<string> SecretNames,
    bool SkipIntegration = false,
    ManagedAgentsSettings? Managed = null);

/// <summary>The outcome of a <see cref="FeedbackRun"/>: the threads resolved, the threads left unresolved, and the Redos spent.
/// The run resolves each thread that it replies to, so the threads left unresolved are the threads with no reply.</summary>
public sealed record FeedbackRunResult(int Resolved, int Unresolved, int Redos);

/// <summary>Answers every unresolved review thread of a pull request within the budget of the <see cref="BuildLoop"/>. The
/// <paramref name="time"/>, the <paramref name="handler"/> and the <paramref name="delay"/> serve the Managed Agents path, when the
/// request holds its settings. A round that builds appends its section <c>## Feedback run</c> to the file at
/// <paramref name="summaryPath"/>, and a write that fails goes to <paramref name="log"/>.</summary>
public sealed class FeedbackRun(
    CommandRunner runner,
    GitHubClient client,
    TextWriter? progress = null,
    TimeProvider? time = null,
    HttpMessageHandler? handler = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    string? summaryPath = null,
    TextWriter? log = null)
{
    /// <summary>The heading of the section of a round in the job summary.</summary>
    public const string SummaryHeading = "Feedback run";

    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private JobSummary _summary = new(progress);
    private IReadOnlyList<SessionCost> _sessions = [];

    /// <summary>True when the last <see cref="RunAsync"/> ended on a closing issue that the recipe rules decline, with a
    /// comment on the pull request and no build.</summary>
    public bool Declined { get; private set; }

    /// <summary>
    /// Builds an answer to each unresolved thread, gates and reviews it, pushes it, and replies to each thread. Then it
    /// resolves each thread that it replied to, fixed or disputed. The reply of a disputed thread states the dispute. A round
    /// that stops on an error or spends its budget posts the comment of the loop on the pull request and pushes nothing. A
    /// <see cref="SessionBranchException"/> posts its message on the pull request, and the run then throws it.
    /// </summary>
    public async Task<FeedbackRunResult> RunAsync(FeedbackRunRequest request, CancellationToken cancellationToken = default)
    {
        Declined = false;
        _summary = new JobSummary(progress);
        _sessions = [];
        try
        {
            return await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (SessionBranchException exception)
        {
            await PostAsync(request, CommentText.Fit(exception.Message, []), cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await WriteSummaryAsync().ConfigureAwait(false);
        }
    }

    private async Task<FeedbackRunResult> RunCoreAsync(FeedbackRunRequest request, CancellationToken cancellationToken)
    {
        var reviewThreads = new ReviewThreads(client);
        IReadOnlyList<ReviewThread> all = await reviewThreads.ReadThreadsAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false);
        List<ReviewThread> threads = [.. all.Where(thread => !thread.IsResolved)];
        if (threads.Count is 0)
        {
            return new FeedbackRunResult(0, 0, 0);
        }

        string branch = await new PullRequests(client).ReadHeadBranchAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false);
        var pullRequests = new PullRequests(client);
        var choice = new RecipeChoice(Recipe.Behavior, [], null);
        string? issueBody = null;
        if (PullRequests.ParseClosingIssue(await pullRequests.ReadBodyAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false)) is int issueNumber)
        {
            GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, issueNumber, cancellationToken).ConfigureAwait(false);
            issueBody = issue.Body;
            choice = RecipeRules.Parse(issue.Labels, issue.Body);
            if (choice.Declined is string refusal)
            {
                await PostAsync(request, CommentText.Fit(refusal, []), cancellationToken).ConfigureAwait(false);
                Declined = true;
                return new FeedbackRunResult(0, threads.Count, 0);
            }
        }

        var git = new Git(runner);
        await Say("clone: start").ConfigureAwait(false);
        await git.CloneAsync($"https://github.com/{request.Owner}/{request.Repo}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
        await git.CheckoutExistingBranchAsync(branch, request.Directory, cancellationToken).ConfigureAwait(false);
        string baseCommit = await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false);
        Pin pin = RecipeRules.TakePin(choice.Recipe, request.Directory);

        string title = $"Answer the review threads of pull request #{request.PullNumber.ToString(CultureInfo.InvariantCulture)}";
        string body = Describe(threads, issueBody, RecipeRules.ThreadRules(choice.Recipe));

        var setup = new RunAgentsSetup(
            runner, git, request.Owner, request.Repo, request.Token, request.Directory, request.RootDirectory, request.ImageTag,
            baseCommit, $"{branch}-session", title, request.AuthorName, request.AuthorEmail, request.Managed, handler,
            delay, time, progress, OnAnswer: (elapsed, answer) => _summary.Prompt(elapsed, answer.NumTurns, answer.CostUsd));
        RunAgents agents = await RunAgents.StartAsync(setup, cancellationToken).ConfigureAwait(false);
        await using (agents.ConfigureAwait(false))
        {
            try
            {
                GateSet gates = await new GateFactory(agents.Sandbox, git, request.Directory, baseCommit, request.SecretNames, progress, skipIntegration: request.SkipIntegration)
                    .CreateAsync(choice, pin, cancellationToken).ConfigureAwait(false);
                if (gates.Stop is string stop)
                {
                    await PostAsync(request, stop, cancellationToken).ConfigureAwait(false);
                    return new FeedbackRunResult(0, threads.Count, 0);
                }

                var build = new FeedbackBuild(
                    new BuildPhase(agents.Builder, RecipeRules.BuildText(choice.Recipe, RunKind.Feedback)),
                    title, body, request.BuildModel, request.BuildTurns, threads);
                var reviewPhase = new ReviewPhase(agents.Reviewer, git, RecipeRules.ReviewSteps(choice.Recipe, RunKind.Feedback));
                var loop = new BuildLoop(_summary, _clock, progress, gates.Lint, request.Directory, title, request.AuthorName, request.AuthorEmail);
                BuildLoopResult result = await loop.RunAsync(
                    build,
                    gates.Gates,
                    token => reviewPhase.RunAsync(title, body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, token),
                    cancellationToken).ConfigureAwait(false);
                if (result.End is not BuildLoopEnd.Passed)
                {
                    await PostAsync(request, result.Comment ?? "", cancellationToken).ConfigureAwait(false);
                    return new FeedbackRunResult(0, threads.Count, result.Redos);
                }

                await agents.CollectAsync(branch, cancellationToken).ConfigureAwait(false);
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
            finally
            {
                _sessions = agents.Sessions;
            }
        }
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    /// <summary>Writes <paramref name="body"/> as a progress line and posts it on the pull request unchanged.</summary>
    private async Task PostAsync(FeedbackRunRequest request, string body, CancellationToken cancellationToken)
    {
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Owner, request.Repo, request.PullNumber, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSummaryAsync()
    {
        if (string.IsNullOrEmpty(summaryPath) || _summary.IsEmpty)
        {
            return;
        }

        _summary.AddSessions(_sessions);
        try
        {
            await JobSummary.AppendToFileAsync(summaryPath, _summary.Render(SummaryHeading)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await ProgressLine.WriteAsync(log, $"summary: cannot write {summaryPath}: {exception.Message}").ConfigureAwait(false);
        }
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
}
