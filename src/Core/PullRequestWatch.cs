namespace Core;

/// <summary>The end of a watch: green or red, the review threads that are not resolved, and the body of a refusal by the reviewer on the head commit, or null.</summary>
public sealed record PullRequestWatchResult(bool IsGreen, IReadOnlyList<ReviewThread> Threads, string? Refusal = null);

/// <summary>Polls GitHub until a pull request is settled or a timeout passes.</summary>
public sealed class PullRequestWatch(
    PullRequests pullRequests,
    ReviewThreads reviewThreads,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeProvider? time = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((span, token) => Task.Delay(span, token));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Polls the check runs and the reviews of a pull request (the check runs alone when <paramref name="reviewerLogin"/> is null), then gives back green or red and the unresolved threads. A refusal by the reviewer on the head commit ends the watch at once with red.</summary>
    public Task<PullRequestWatchResult> WatchAsync(string owner, string repo, int number, string? reviewerLogin, TimeSpan wait, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        WatchAsync(owner, repo, number, reviewerLogin, wait, timeout, 0, null, cancellationToken);

    /// <summary>Polls like the watch without the skip, but the first <paramref name="skippedReviews"/> reviews by the reviewer on <paramref name="skippedHead"/>, the ones that existed when a new review was requested, count for neither the refusal nor the settled check.</summary>
    public async Task<PullRequestWatchResult> WatchAsync(string owner, string repo, int number, string? reviewerLogin, TimeSpan wait, TimeSpan timeout, int skippedReviews, string? skippedHead, CancellationToken cancellationToken = default)
    {
        long start = _time.GetTimestamp();
        bool green;
        string? refusal = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sha = await pullRequests.ReadHeadShaAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<CheckRun> runs = await pullRequests.ReadCheckRunsAsync(owner, repo, sha, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Review> reviews = reviewerLogin is null
                ? []
                : await reviewThreads.ReadReviewsAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
            if (reviewerLogin is not null && skippedReviews > 0 && sha == skippedHead)
            {
                reviews = [.. SkipOld(reviews, reviewerLogin, sha, skippedReviews)];
            }

            if (reviewerLogin is not null && Review.Refusal(reviews, reviewerLogin, sha) is { } refused)
            {
                green = false;
                refusal = refused;
                break;
            }

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
        return new PullRequestWatchResult(green, [.. threads.Where(t => !t.IsResolved)], refusal);
    }

    private static IEnumerable<Review> SkipOld(IReadOnlyList<Review> reviews, string reviewerLogin, string sha, int skipped)
    {
        int seen = 0;
        foreach (Review review in reviews)
        {
            if (review.Author == reviewerLogin && review.CommitSha.Length is not 0 && review.CommitSha == sha && seen++ < skipped)
            {
                continue;
            }

            yield return review;
        }
    }
}
