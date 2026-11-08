using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core;

/// <summary>How a settle ended.</summary>
public enum SettleEnd
{
    /// <summary>Green, Copilot reviewed the head with no refusal, no unresolved thread.</summary>
    Settled,

    /// <summary>The last watch was red, timed out, refused, or left a thread, and the rounds are spent or the last round pushed nothing.</summary>
    Unsettled,

    /// <summary>A run declined its issue and posted why.</summary>
    Declined,

    /// <summary>A run stopped before a pass: a gate, the loop budget or a session branch.</summary>
    Stopped,
}

/// <summary>The end of a settle, the last watch or null when no watch ran, and the feedback rounds it took.</summary>
public sealed record SettleResult(SettleEnd End, PullRequestWatchResult? Last, int Rounds);

/// <summary>What both openings share.</summary>
public sealed record SettleOptions(int Rounds, bool CopilotReview, TimeSpan Timeout, IReadOnlyList<string> SecretNames, bool SkipIntegration);

/// <summary>The fixed choices of a settle: the model and the turn budgets of each agent, the commit author, the reviewer, and each wait.</summary>
public sealed record SettleSettings(
    string Model, int PlanTurns, int BuildTurns, int ReviewTurns,
    string AuthorName, string AuthorEmail,
    string Reviewer, TimeSpan PollWait, TimeSpan HeadGrace, TimeSpan HeadPoll)
{
    /// <summary>The choices of the console program.</summary>
    public static SettleSettings Default { get; } = new(
        "claude-sonnet-5-5", 50, 200, 50, "dotnet-software-factory", "dotnet-software-factory@noreply.github.com",
        "copilot-pull-request-reviewer", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
}

/// <summary>
/// Drives an issue or a pull request to settled, as the glossary defines it. An opening run comes first: a factory run on the issue,
/// or one feedback run on the pull request. Then the tail watches the pull request, and runs another feedback round while the last
/// watch had no refusal, left an unresolved thread, and the budget of <see cref="SettleOptions.Rounds"/> is not spent. Each run
/// clones into a new temporary directory, which the settle removes when the run ends. Every progress line goes to
/// <paramref name="progress"/>: the pull request URL, <c>watch: start</c>, <c>feedback: start</c>, <c>review: refused: ...</c>
/// and <c>checks: ..., unresolved threads: n</c>. <paramref name="token"/> is the token of each clone and push.
/// </summary>
public sealed class Settle(
    CommandRunner runner,
    GitHubClient client,
    GitHubClient checksClient,
    string token,
    StartBackend startBackend,
    SettleSettings settings,
    TextWriter progress,
    TextWriter log,
    TimeProvider time,
    Func<TimeSpan, CancellationToken, Task> delay,
    string? summaryPath)
{
    private readonly PullRequests _pullRequests = new(client, checksClient, delay, time);

    /// <summary>A factory run on the issue, then the tail. <paramref name="baseCommit"/> is the full SHA of the commit the run builds
    /// on, or null for the head of the default branch. A run that declines or stops ends the settle with no watch.</summary>
    public async Task<SettleResult> SettleIssueAsync(IssueNumber issue, string? baseCommit, SettleOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(options);
        JsonElement repoJson = await client.SendAsync(HttpMethod.Get, $"/repos/{issue.Repo.Owner}/{issue.Repo.Name}", null, cancellationToken).ConfigureAwait(false);
        string baseBranch = repoJson.GetProperty("default_branch").GetString() ?? "";
        string runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-run-");
        RunResult<Delivered> result;
        try
        {
            var request = new FactoryRunRequest(
                issue, token, runId, directory.FullName, baseBranch,
                settings.Model, settings.PlanTurns, settings.Model, settings.BuildTurns, settings.Model, settings.ReviewTurns,
                settings.AuthorName, settings.AuthorEmail, options.SecretNames, options.SkipIntegration, baseCommit);
            var factoryRun = new FactoryRun(runner, client, startBackend, progress, log, time, summaryPath, EntryVersion());
            result = await factoryRun.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Remove(directory);
        }

        if (result.Passed is not Delivered delivered)
        {
            return new SettleResult(End(result.Stop), null, 0);
        }

        await progress.WriteLineAsync(delivered.PullRequest.HtmlUrl).ConfigureAwait(false);
        return await TailAsync(new IssueNumber(issue.Repo, delivered.PullRequest.Number), delivered.Head, options, 0, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One feedback run on the pull request, then the tail. A run that declines its closing issue ends the settle with no
    /// watch. A run that stops still goes to the tail, since the pull request stands as it was. The opening run counts as one of
    /// <see cref="SettleResult.Rounds"/>, and not against the budget of <see cref="SettleOptions.Rounds"/>.</summary>
    public async Task<SettleResult> SettlePullRequestAsync(IssueNumber pullRequest, SettleOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        ArgumentNullException.ThrowIfNull(options);
        RunResult<Answered> opening = await FeedbackAsync(pullRequest, options, cancellationToken).ConfigureAwait(false);
        if (opening.Stop?.End is RunEnd.Declined)
        {
            return new SettleResult(SettleEnd.Declined, null, 1);
        }

        return await TailAsync(pullRequest, opening.Passed?.Head, options, 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The watch and the feedback rounds after the opening run. <paramref name="pushed"/> is the head the opening run pushed,
    /// or null. A round that pushed nothing ends the loop with the last watch as the result. A round that declines or stops ends the
    /// settle with its <see cref="RunEnd"/>.</summary>
    private async Task<SettleResult> TailAsync(IssueNumber pullRequest, string? pushed, SettleOptions options, int rounds, CancellationToken cancellationToken)
    {
        PullRequestWatchResult last = await WatchAsync(pullRequest, pushed, options, cancellationToken).ConfigureAwait(false);
        int spent = 0;
        while (last.Refusal is null && last.Threads.Count > 0 && spent < options.Rounds)
        {
            RunResult<Answered> round = await FeedbackAsync(pullRequest, options, cancellationToken).ConfigureAwait(false);
            spent++;
            if (round.Stop is { } stop)
            {
                return new SettleResult(End(stop), last, rounds + spent);
            }

            if (round.Passed?.Head is not string head)
            {
                break;
            }

            last = await WatchAsync(pullRequest, head, options, cancellationToken).ConfigureAwait(false);
        }

        if (last.Refusal is not null)
        {
            await progress.WriteLineAsync($"review: refused: {last.Refusal}").ConfigureAwait(false);
        }

        await progress.WriteLineAsync($"checks: {(last.IsGreen ? "green" : "red")}, unresolved threads: {last.Threads.Count.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        SettleEnd end = last.IsGreen && last.Threads.Count is 0 ? SettleEnd.Settled : SettleEnd.Unsettled;
        return new SettleResult(end, last, rounds + spent);
    }

    /// <summary>Runs one feedback run in a new work tree, and removes the work tree after it.</summary>
    private async Task<RunResult<Answered>> FeedbackAsync(IssueNumber pullRequest, SettleOptions options, CancellationToken cancellationToken)
    {
        await progress.WriteLineAsync("feedback: start").ConfigureAwait(false);
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-feedback-");
        try
        {
            var request = new FeedbackRunRequest(
                pullRequest, token, directory.FullName, settings.Model, settings.BuildTurns, settings.Model, settings.ReviewTurns,
                settings.AuthorName, settings.AuthorEmail, options.SecretNames, options.SkipIntegration);
            var feedbackRun = new FeedbackRun(runner, client, startBackend, progress, time, summaryPath, log);
            return await feedbackRun.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Remove(directory);
        }
    }

    /// <summary>Waits for GitHub to read the head <paramref name="pushed"/>, when there is one, then watches the pull request.</summary>
    private async Task<PullRequestWatchResult> WatchAsync(IssueNumber pullRequest, string? pushed, SettleOptions options, CancellationToken cancellationToken)
    {
        await progress.WriteLineAsync("watch: start").ConfigureAwait(false);
        if (pushed is not null)
        {
            await _pullRequests.ReadHeadShaAsync(pullRequest, pushed, settings.HeadGrace, settings.HeadPoll, cancellationToken).ConfigureAwait(false);
        }

        var watch = new PullRequestWatch(_pullRequests, new ReviewThreads(client), delay, time);
        return await watch.WatchAsync(pullRequest, options.CopilotReview ? settings.Reviewer : null, settings.PollWait, options.Timeout, cancellationToken).ConfigureAwait(false);
    }

    private static SettleEnd End(RunStop? stop) => stop?.End is RunEnd.Declined ? SettleEnd.Declined : SettleEnd.Stopped;

    /// <summary>The informational version of the entry assembly, the text that <c>--version</c> prints.</summary>
    private static string? EntryVersion() =>
        (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly())
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;

    private static void Remove(DirectoryInfo directory)
    {
        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
