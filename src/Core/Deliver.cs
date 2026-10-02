using System.Globalization;

namespace Core;

/// <summary>Delivers a run: pushes its branch, opens a pull request and links it from the issue.</summary>
public sealed class Deliver(Git git, PullRequests pullRequests, GitHubClient client)
{
    /// <summary>Pushes <c>factory/&lt;runId&gt;</c>, opens a pull request that closes the issue and comments its web address on the issue.</summary>
    public async Task<PullRequest> RunAsync(string workTree, string runId, string owner, string repo, string baseBranch, int issueNumber, string issueTitle, string token, CancellationToken cancellationToken = default)
    {
        string branch = "factory/" + runId;
        await git.CheckoutBranchAsync(branch, workTree, cancellationToken).ConfigureAwait(false);
        await git.PushAsync(branch, workTree, token, cancellationToken).ConfigureAwait(false);

        string body = string.Create(CultureInfo.InvariantCulture, $"Closes #{issueNumber}");
        PullRequest pullRequest = await pullRequests.OpenAsync(owner, repo, branch, baseBranch, issueTitle, body, cancellationToken).ConfigureAwait(false);
        await client.PostCommentAsync(owner, repo, issueNumber, $"Opened {pullRequest.HtmlUrl}", cancellationToken).ConfigureAwait(false);
        return pullRequest;
    }
}
