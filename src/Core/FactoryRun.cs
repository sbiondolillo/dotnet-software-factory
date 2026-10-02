namespace Core;

/// <summary>What a <see cref="FactoryRun"/> needs: the issue, the clone, the models and the author.</summary>
public sealed record FactoryRunRequest(
    string Owner,
    string Repo,
    int IssueNumber,
    string Token,
    string RunId,
    string Directory,
    string BaseBranch,
    string ImageTag,
    string RootDirectory,
    string PlanModel,
    int PlanTurns,
    string BuildModel,
    int BuildTurns,
    string ReviewModel,
    int ReviewTurns,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<string> SecretNames);

/// <summary>The outcome of a <see cref="FactoryRun"/>: the pull request, or null for none, and the Redos spent.</summary>
public sealed record FactoryRunResult(PullRequest? PullRequest, int Redos);

/// <summary>Takes one issue of a target repo to a pull request within three Redos.</summary>
public sealed class FactoryRun(CommandRunner runner, GitHubClient client)
{
    private const int RedoBudget = 3;

    private static readonly string[] LayoutFiles = ["Solution.slnx"];
    private static readonly string[] LayoutDirectories = ["specs/features/", "tests/Specs/", "tests/Integration/"];

    /// <summary>Runs the issue through the plan, the build, the gates and the review, and delivers an approved build.</summary>
    public async Task<FactoryRunResult> RunAsync(FactoryRunRequest request, CancellationToken cancellationToken = default)
    {
        GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, request.IssueNumber, cancellationToken).ConfigureAwait(false);
        var git = new Git(runner);
        await git.CloneAsync($"https://github.com/{request.Owner}/{request.Repo}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
        string baseCommit = await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false);

        List<string> absent = [];
        absent.AddRange(LayoutFiles.Where(path => !File.Exists(Path.Combine(request.Directory, path))));
        absent.AddRange(LayoutDirectories.Where(path => !Directory.Exists(Path.Combine(request.Directory, path))));
        if (absent.Count > 0)
        {
            await CommentAsync(request, "The clone lacks these paths of the layout:", absent, cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        Sandbox sandbox = await Sandbox.StartAsync(runner, request.ImageTag, request.RootDirectory, request.Directory, cancellationToken).ConfigureAwait(false);
        await using (sandbox.ConfigureAwait(false))
        {
            var agent = new ClaudeCodeAgent(sandbox);
            PlanPhaseResult plan = await new PlanPhase(agent)
                .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
            if (!plan.Passed || plan.Pin is null)
            {
                await CommentAsync(request, "The plan phase failed:", plan.Failures, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }

            var buildPhase = new BuildPhase(agent);
            var lintGate = new LintGate(sandbox, git);
            var testGate = new TestGate(sandbox);
            var reviewPhase = new ReviewPhase(agent, git);

            int redos = 0;
            IReadOnlyList<string> redoTexts = [];
            while (true)
            {
                await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
                await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
                TestGateResult gate = await testGate.RunAsync(request.Directory, plan.Pin, request.SecretNames, cancellationToken).ConfigureAwait(false);
                if (gate.Passed)
                {
                    ReviewPhaseResult review = await reviewPhase
                        .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                    if (review.Approved)
                    {
                        var deliver = new Deliver(git, new PullRequests(client), client);
                        PullRequest pullRequest = await deliver
                            .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                        return new FactoryRunResult(pullRequest, redos);
                    }

                    redoTexts = review.Blocking;
                }
                else
                {
                    redoTexts = gate.Evidence;
                }

                if (redos >= RedoBudget)
                {
                    await CommentAsync(request, $"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts, cancellationToken).ConfigureAwait(false);
                    return new FactoryRunResult(null, redos);
                }

                redos++;
            }
        }
    }

    private async Task CommentAsync(FactoryRunRequest request, string lead, IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        await client.PostCommentAsync(request.Owner, request.Repo, request.IssueNumber, CommentText.Fit(lead, texts), cancellationToken).ConfigureAwait(false);
}
