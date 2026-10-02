using System.Diagnostics;

namespace Core;

/// <summary>The end of a watch: green or red, and the review threads that are not resolved.</summary>
public sealed record PullRequestWatchResult(bool IsGreen, IReadOnlyList<ReviewThread> Threads);

/// <summary>Polls GitHub until a pull request is settled or a timeout passes.</summary>
public sealed class PullRequestWatch(PullRequests pullRequests, ReviewThreads reviewThreads)
{
    /// <summary>Polls the check runs and the reviews of a pull request, then gives back green or red and the unresolved threads.</summary>
    public async Task<PullRequestWatchResult> WatchAsync(string owner, string repo, int number, string reviewerLogin, TimeSpan wait, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        long start = Stopwatch.GetTimestamp();
        bool green;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sha = await pullRequests.ReadHeadShaAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<CheckRun> runs = await pullRequests.ReadCheckRunsAsync(owner, repo, sha, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Review> reviews = await reviewThreads.ReadReviewsAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
            bool settled = runs.Count is not 0
                && runs.All(r => r.Status is "completed")
                && reviews.Any(r => r.Author == reviewerLogin);
            if (settled)
            {
                green = runs.All(r => r.Conclusion is "success" or "skipped");
                break;
            }

            if (Stopwatch.GetElapsedTime(start) >= timeout)
            {
                green = false;
                break;
            }

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<ReviewThread> threads = await reviewThreads.ReadThreadsAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
        return new PullRequestWatchResult(green, threads.Where(t => !t.IsResolved).ToList());
    }
}
