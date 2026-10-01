using System.Globalization;

namespace Core;

/// <summary>Delivers a run: pushes its branch, opens a pull request that closes the issue, and comments on the issue.</summary>
public sealed class Deliver(Git git, PullRequests pullRequests, GitHubClient client)
{
    /// <summary>Delivers the run <paramref name="runId"/> from <paramref name="workTree"/> and gives back the pull request.</summary>
    public async Task<PullRequest> DeliverAsync(string workTree, string runId, string owner, string repo, string baseBranch, int issueNumber, string issueTitle, string token, CancellationToken cancellationToken = default)
    {
        string branch = "factory/" + runId;
        await git.CheckoutBranchAsync(branch, workTree, cancellationToken).ConfigureAwait(false);
        await git.PushAsync(branch, workTree, token, cancellationToken).ConfigureAwait(false);
        string number = issueNumber.ToString(CultureInfo.InvariantCulture);
        PullRequest pullRequest = await pullRequests.OpenAsync(owner, repo, branch, baseBranch, issueTitle, "Closes #" + number + "\n", cancellationToken).ConfigureAwait(false);
        await client.PostCommentAsync(owner, repo, issueNumber, "Opened " + pullRequest.HtmlUrl, cancellationToken).ConfigureAwait(false);
        return pullRequest;
    }
}
