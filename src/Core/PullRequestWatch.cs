namespace Core;

/// <summary>The end of a watch: green or red, and the review threads that are not resolved.</summary>
public sealed record PullRequestWatchResult(bool IsGreen, IReadOnlyList<ReviewThread> Threads);

/// <summary>Polls GitHub until a pull request is settled or a timeout passes.</summary>
public sealed class PullRequestWatch(
    PullRequests pullRequests,
    ReviewThreads reviewThreads,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeProvider? time = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((span, token) => Task.Delay(span, token));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Polls the check runs and the reviews of a pull request (the check runs alone when <paramref name="reviewerLogin"/> is null), then gives back green or red and the unresolved threads.</summary>
    public async Task<PullRequestWatchResult> WatchAsync(string owner, string repo, int number, string? reviewerLogin, TimeSpan wait, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        long start = _time.GetTimestamp();
        bool green;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sha = await pullRequests.ReadHeadShaAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<CheckRun> runs = await pullRequests.ReadCheckRunsAsync(owner, repo, sha, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Review> reviews = reviewerLogin is null
                ? []
                : await reviewThreads.ReadReviewsAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
            bool settled = runs.Count is not 0
                && runs.All(r => r.Status is "completed")
                && (reviewerLogin is null || reviews.Any(r => r.Author == reviewerLogin && r.CommitSha.Length is not 0 && r.CommitSha == sha));
            if (settled)
            {
                green = runs.All(r => r.Conclusion is "success" or "skipped");
                break;
            }

            if (_time.GetElapsedTime(start) >= timeout)
            {
                green = false;
                break;
            }

            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<ReviewThread> threads = await reviewThreads.ReadThreadsAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
        return new PullRequestWatchResult(green, [.. threads.Where(t => !t.IsResolved)]);
    }
}
