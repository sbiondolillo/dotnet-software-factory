using System.Globalization;

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
public sealed class FactoryRun(CommandRunner runner, GitHubClient client, TextWriter? progress = null)
{
    private const int RedoBudget = 3;
    private const string CleanupLabel = "cleanup";

    private static readonly string[] LayoutFiles = ["Solution.slnx"];
    private static readonly string[] LayoutDirectories = ["specs/features/", "tests/Specs/", "tests/Integration/"];

    /// <summary>Runs the issue through the plan, the build, the gates and the review, and delivers an approved build.</summary>
    public async Task<FactoryRunResult> RunAsync(FactoryRunRequest request, CancellationToken cancellationToken = default)
    {
        GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, request.IssueNumber, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> scope = [];
        string check = "";
        bool cleanup = issue.Labels.Contains(CleanupLabel);
        if (cleanup)
        {
            string? refusal = null;
            if (issue.Labels.Contains("behavior"))
            {
                refusal = "The labels cleanup and behavior exclude each other: a cleanup run has no plan, and a behavior run has one. Keep one of the two labels.";
            }
            else if ((scope = CleanupBody.ReadScope(issue.Body)).Count is 0)
            {
                refusal = "The body of a cleanup issue names its scope under the heading ## Scope, one path for each line. The body holds no such heading with a path under it.";
            }
            else if (CleanupBody.ReadCheck(issue.Body) is not string command)
            {
                refusal = "The body of a cleanup issue holds its check command in the first fenced block under the heading ## Check. The body holds no such heading with a fenced block under it.";
            }
            else
            {
                check = command;
            }

            if (refusal is not null)
            {
                await CommentAsync(request, refusal, [], cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }
        }

        var git = new Git(runner);
        await Say("clone: start").ConfigureAwait(false);
        await git.CloneAsync($"https://github.com/{request.Owner}/{request.Repo}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
        string baseCommit = await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false);

        List<string> absent = [];
        absent.AddRange(LayoutFiles.Where(path => !File.Exists(Path.Combine(request.Directory, path))));
        absent.AddRange(LayoutDirectories.Where(path => !Directory.Exists(Path.Combine(request.Directory, path))));
        if (absent.Count > 0)
        {
            await Say("layout: failed").ConfigureAwait(false);
            await CommentAsync(request, "The clone lacks these paths of the layout:", absent, cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        await Say("sandbox: start").ConfigureAwait(false);
        Sandbox sandbox = await Sandbox.StartAsync(runner, request.ImageTag, request.RootDirectory, request.Directory, cancellationToken).ConfigureAwait(false);
        await using (sandbox.ConfigureAwait(false))
        {
            var agent = new ClaudeCodeAgent(sandbox);
            if (cleanup)
            {
                return await RunCleanupAsync(request, issue, scope, check, sandbox, agent, git, baseCommit, cancellationToken).ConfigureAwait(false);
            }

            await Say("plan: start").ConfigureAwait(false);
            PlanPhaseResult plan = await new PlanPhase(agent)
                .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
            if (!plan.Passed || plan.Pin is null)
            {
                await Say("plan: failed").ConfigureAwait(false);
                await CommentAsync(request, "The plan phase failed:", plan.Failures, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }

            await Say("plan: passed").ConfigureAwait(false);
            var buildPhase = new BuildPhase(agent);
            var lintGate = new LintGate(sandbox, git);
            var testGate = new TestGate(sandbox);
            var reviewPhase = new ReviewPhase(agent, git);

            int redos = 0;
            IReadOnlyList<string> redoTexts = [];
            while (true)
            {
                await Say("build: start").ConfigureAwait(false);
                await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
                await Say("lint: start").ConfigureAwait(false);
                await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
                await Say("test: start").ConfigureAwait(false);
                TestGateResult gate = await testGate.RunAsync(request.Directory, plan.Pin, request.SecretNames, cancellationToken).ConfigureAwait(false);
                await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
                if (gate.Passed)
                {
                    await Say("review: start").ConfigureAwait(false);
                    ReviewPhaseResult review = await reviewPhase
                        .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                    await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                    if (review.Approved)
                    {
                        await Say("deliver: start").ConfigureAwait(false);
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
                await Say($"redo: {redos}").ConfigureAwait(false);
            }
        }
    }

    private async Task<FactoryRunResult> RunCleanupAsync(
        FactoryRunRequest request,
        GitHubIssue issue,
        IReadOnlyList<string> scope,
        string check,
        Sandbox sandbox,
        ClaudeCodeAgent agent,
        Git git,
        string baseCommit,
        CancellationToken cancellationToken)
    {
        CommandResult first = await RunCheckAsync(sandbox, check, cancellationToken).ConfigureAwait(false);
        await Say(first.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
        if (first.ExitCode is 0)
        {
            await CommentAsync(request, "The check passes before the build, so there is nothing to clean up.", [], cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        if (first.ExitCode is 126 or 127)
        {
            await CommentAsync(request, $"The check command exited with the code {first.ExitCode} before the build, so it did not run. Its standard error:", [first.StandardError], cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        var buildPhase = new BuildPhase(agent, cleanup: true);
        var lintGate = new LintGate(sandbox, git);
        var testGate = new TestGate(sandbox);
        var reviewPhase = new ReviewPhase(agent, git, cleanup: true);
        Pin pin = Pin.Take(request.Directory);
        IReadOnlySet<string> basePassed = await testGate.ReadPassedTestsAsync(request.Directory, cancellationToken).ConfigureAwait(false);

        int redos = 0;
        IReadOnlyList<string> redoTexts = [];
        while (true)
        {
            await Say("build: start").ConfigureAwait(false);
            await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
            await Say("lint: start").ConfigureAwait(false);
            await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
            redoTexts = await ScopeFaultsAsync(git, baseCommit, request.Directory, scope, cancellationToken).ConfigureAwait(false);
            if (redoTexts.Count is 0)
            {
                await Say("test: start").ConfigureAwait(false);
                TestGateResult gate = await testGate
                    .RunAsync(request.Directory, pin, request.SecretNames, path => CleanupBody.InScope(scope, path) && !path.StartsWith("specs/features/", StringComparison.Ordinal), cancellationToken)
                    .ConfigureAwait(false);
                await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
                if (!gate.Passed)
                {
                    redoTexts = gate.Evidence;
                }
                else
                {
                    HashSet<string> nowPassed = [.. (gate.Tests ?? []).Where(test => test.Outcome is TestOutcome.Passed).Select(test => $"{test.ClassName}.{test.Method}")];
                    redoTexts = [.. basePassed.Where(name => !nowPassed.Contains(name)).Order(StringComparer.Ordinal).Select(name => $"lost test: {name}")];
                }

                if (redoTexts.Count is 0)
                {
                    CommandResult after = await RunCheckAsync(sandbox, check, cancellationToken).ConfigureAwait(false);
                    await Say(after.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
                    if (after.ExitCode is 0)
                    {
                        await Say("review: start").ConfigureAwait(false);
                        ReviewPhaseResult review = await reviewPhase
                            .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                        await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                        if (review.Approved)
                        {
                            await Say("deliver: start").ConfigureAwait(false);
                            var deliver = new Deliver(git, new PullRequests(client), client);
                            PullRequest pullRequest = await deliver
                                .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                            return new FactoryRunResult(pullRequest, redos);
                        }

                        redoTexts = review.Blocking;
                    }
                    else
                    {
                        redoTexts =
                        [
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"The check command exited with the code {after.ExitCode}\nstandard output: {after.StandardOutput}\nstandard error: {after.StandardError}"),
                        ];
                    }
                }
            }

            if (redos >= RedoBudget)
            {
                await CommentAsync(request, $"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, redos);
            }

            redos++;
            await Say($"redo: {redos}").ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> ScopeFaultsAsync(
        Git git,
        string baseCommit,
        string directory,
        IReadOnlyList<string> scope,
        CancellationToken cancellationToken)
    {
        string[] changed = await git.ListChangedFilesAsync(baseCommit, directory, cancellationToken).ConfigureAwait(false);
        List<string> faults = [];
        foreach (string path in changed.Where(path => !path.StartsWith(".factory/", StringComparison.Ordinal)))
        {
            if (path.StartsWith("specs/features/", StringComparison.Ordinal))
            {
                faults.Add($"pin: {path}");
            }
            else if (!CleanupBody.InScope(scope, path))
            {
                faults.Add($"outside the scope: {path}");
            }
        }

        return faults;
    }

    private async Task<CommandResult> RunCheckAsync(Sandbox sandbox, string command, CancellationToken cancellationToken)
    {
        await Say("check: start").ConfigureAwait(false);
        string copy = $"/tmp/check-{Guid.NewGuid():N}";
        CommandResult add = await sandbox.RunAsync("git", ["worktree", "add", "--detach", copy, "HEAD"], [], cancellationToken).ConfigureAwait(false);
        if (add.ExitCode is not 0)
        {
            throw new SandboxException(add.StandardError);
        }

        try
        {
            return await sandbox.RunAsync("sh", ["-c", command], [], copy, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await sandbox.RunAsync("git", ["worktree", "remove", "--force", copy], [], CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    private async Task CommentAsync(FactoryRunRequest request, string lead, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        string body = CommentText.Fit(lead, texts);
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Owner, request.Repo, request.IssueNumber, body, cancellationToken).ConfigureAwait(false);
    }
}
