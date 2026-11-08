namespace Core;

/// <summary>The end of a watch: green or red, the review threads that are not resolved, and the body of a refusal by the reviewer on the head commit, or null.</summary>
public sealed record PullRequestWatchResult(bool IsGreen, IReadOnlyList<ReviewThread> Threads, string? Refusal = null);

/// <summary>Polls GitHub until a pull request is settled or a timeout passes, and requests the review that it waits for.</summary>
public sealed class PullRequestWatch(
    PullRequests pullRequests,
    ReviewThreads reviewThreads,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeProvider? time = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((span, token) => Task.Delay(span, token));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Polls the check runs and the reviews of a pull request (the check runs alone when <paramref name="reviewerLogin"/> is null),
    /// then gives back green or red and the unresolved threads. Before the poll, when the reviewer has no review on the head commit
    /// or a refusal is its last review there, the watch requests a review by the reviewer, and the reviews that existed then count for
    /// nothing. A refusal by the reviewer on the head commit ends the watch at once with red.
    /// </summary>
    public async Task<PullRequestWatchResult> WatchAsync(IssueNumber pullRequest, string? reviewerLogin, TimeSpan wait, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        int skipped = 0;
        string? skippedHead = null;
        if (reviewerLogin is not null)
        {
            string head = await pullRequests.ReadHeadShaAsync(pullRequest, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Review> reviews = await reviewThreads.ReadReviewsAsync(pullRequest, cancellationToken).ConfigureAwait(false);
            int onHead = reviews.Count(r => r.Author == reviewerLogin && r.CommitSha.Length is not 0 && r.CommitSha == head);
            if (onHead is 0 || Review.Refusal(reviews, reviewerLogin, head) is not null)
            {
                string nodeId = await pullRequests.ReadNodeIdAsync(pullRequest, cancellationToken).ConfigureAwait(false);
                await reviewThreads.RequestReviewAsync(nodeId, reviewerLogin, cancellationToken).ConfigureAwait(false);
                (skipped, skippedHead) = (onHead, head);
            }
        }

        return await WatchAsync(pullRequest, reviewerLogin, wait, timeout, skipped, skippedHead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Polls as the public watch states, but the first <paramref name="skippedReviews"/> reviews by the reviewer on <paramref name="skippedHead"/>, the ones that existed when a new review was requested, count for neither the refusal nor the settled check.</summary>
    private async Task<PullRequestWatchResult> WatchAsync(IssueNumber pullRequest, string? reviewerLogin, TimeSpan wait, TimeSpan timeout, int skippedReviews, string? skippedHead, CancellationToken cancellationToken)
    {
        long start = _time.GetTimestamp();
        bool green;
        string? refusal = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sha = await pullRequests.ReadHeadShaAsync(pullRequest, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<CheckRun> runs = await pullRequests.ReadCheckRunsAsync(pullRequest.Repo, sha, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Review> reviews = reviewerLogin is null
                ? []
                : await reviewThreads.ReadReviewsAsync(pullRequest, cancellationToken).ConfigureAwait(false);
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

        IReadOnlyList<ReviewThread> threads = await reviewThreads.ReadThreadsAsync(pullRequest, cancellationToken).ConfigureAwait(false);
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
