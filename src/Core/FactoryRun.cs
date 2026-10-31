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
    IReadOnlyList<string> SecretNames,
    bool SkipIntegration = false,
    ManagedAgentsSettings? Managed = null);

/// <summary>The outcome of a <see cref="FactoryRun"/>: the pull request, or null for none, and the Redos spent.</summary>
public sealed record FactoryRunResult(PullRequest? PullRequest, int Redos);

/// <summary>Takes one issue of a target repo to a pull request within three Redos. The <paramref name="handler"/> and the
/// <paramref name="delay"/> serve the Managed Agents path, when the request holds its settings.</summary>
public sealed class FactoryRun(
    CommandRunner runner,
    GitHubClient client,
    TextWriter? progress = null,
    TextWriter? log = null,
    TimeProvider? time = null,
    string? summaryPath = null,
    string? version = null,
    HttpMessageHandler? handler = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const int RedoBudget = 3;
    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private JobSummary _summary = new();
    private decimal _spend;
    private long _started;
    private IReadOnlyList<SessionCost> _sessions = [];
    private const string CleanupLabel = "cleanup";
    private const string RefactorLabel = "refactor";
    private const string ReframeLabel = "reframe";

    private static readonly string[] LayoutFiles = ["Solution.slnx"];
    private static readonly string[] LayoutDirectories = ["specs/features/", "tests/Specs/", "tests/Integration/"];

    /// <summary>Runs the issue through the plan, the build, the gates and the review, and delivers an approved build.</summary>
    public async Task<FactoryRunResult> RunAsync(FactoryRunRequest request, CancellationToken cancellationToken = default)
    {
        _summary = new JobSummary();
        _spend = 0m;
        _sessions = [];
        _started = _clock.GetTimestamp();
        try
        {
            return await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await WriteSummaryAsync().ConfigureAwait(false);
        }
    }

    private async Task<FactoryRunResult> RunCoreAsync(FactoryRunRequest request, CancellationToken cancellationToken)
    {
        GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, request.IssueNumber, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> scope = [];
        string check = "";
        bool reframe = issue.Labels.Contains(ReframeLabel);
        if (reframe)
        {
            string? refusal = null;
            if (new[] { "behavior", CleanupLabel, RefactorLabel }.FirstOrDefault(issue.Labels.Contains) is string other)
            {
                refusal = $"The labels reframe and {other} exclude each other: each one names a kind of run. Keep one of the two labels.";
            }
            else if ((scope = CleanupBody.ReadScope(issue.Body)).Count is 0)
            {
                refusal = "The body of a reframe issue names its scope under the heading ## Scope, one path for each line. The body holds no such heading with a path under it.";
            }
            else if (CleanupBody.ReadCheck(issue.Body) is not string command)
            {
                refusal = "The body of a reframe issue holds its check command in the first fenced block under the heading ## Check. The body holds no such heading with a fenced block under it.";
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

        bool refactor = issue.Labels.Contains(RefactorLabel);
        if (refactor && (issue.Labels.Contains("behavior") || issue.Labels.Contains(CleanupLabel)))
        {
            string other = issue.Labels.Contains("behavior") ? "behavior" : CleanupLabel;
            await CommentAsync(request, $"The labels refactor and {other} exclude each other: a refactor run leaves every test as it is, and a {other} run does not. Keep one of the two labels.", [], cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

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

        IssueScenariosResult? scenarios = null;
        if (!reframe && !refactor && !cleanup && IssueScenarios.Holds(issue.Body))
        {
            scenarios = IssueScenarios.Apply(issue.Body, request.Directory);
            if (scenarios.Faults.Count > 0)
            {
                await CommentAsync(request, "The scenarios of the issue are not sound, so there is no run:", scenarios.Faults, cancellationToken).ConfigureAwait(false);
                await Say("scenarios: failed").ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }
        }

        var setup = new RunAgentsSetup(
            runner, git, request.Owner, request.Repo, request.Token, request.Directory, request.RootDirectory, request.ImageTag,
            baseCommit, $"factory/{request.RunId}-session", issue.Title, request.AuthorName, request.AuthorEmail,
            request.Managed, handler, delay, time, progress, log,
            (elapsed, answer) =>
            {
                _spend += answer.CostUsd;
                _summary.Prompt(elapsed, answer.NumTurns, answer.CostUsd);
            });
        RunAgents agents = await RunAgents.StartAsync(setup, cancellationToken).ConfigureAwait(false);
        await using (agents.ConfigureAwait(false))
        {
            try
            {
                return await RunAgentsAsync(request, issue, scope, check, scenarios, reframe, refactor, cleanup, agents, git, baseCommit, cancellationToken).ConfigureAwait(false);
            }
            catch (SessionBranchException exception)
            {
                return await StopAsync(request, exception.Message, 0, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sessions = agents.Sessions;
            }
        }
    }

    private async Task<FactoryRunResult> RunAgentsAsync(
        FactoryRunRequest request,
        GitHubIssue issue,
        IReadOnlyList<string> scope,
        string check,
        IssueScenariosResult? scenarios,
        bool reframe,
        bool refactor,
        bool cleanup,
        RunAgents agents,
        Git git,
        string baseCommit,
        CancellationToken cancellationToken)
    {
        if (reframe)
        {
            return await RunReframeAsync(request, issue, scope, check, agents, git, baseCommit, cancellationToken).ConfigureAwait(false);
        }

        if (refactor)
        {
            return await RunRefactorAsync(request, issue, agents, git, baseCommit, cancellationToken).ConfigureAwait(false);
        }

        if (cleanup)
        {
            return await RunCleanupAsync(request, issue, scope, check, agents, git, baseCommit, cancellationToken).ConfigureAwait(false);
        }

        Agent agent = agents.Builder;
        await Say("plan: start").ConfigureAwait(false);
        PlanPhaseResult plan = await new PlanPhase(agent, scenarios: scenarios)
            .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
        if (!plan.Passed || plan.Pin is null)
        {
            _summary.AddRow("plan", null, "failed");
            await Say("plan: failed").ConfigureAwait(false);
            await CommentAsync(request, "The plan phase failed:", plan.Failures, cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        _summary.AddRow("plan", null, "passed");
        await Say("plan: passed").ConfigureAwait(false);
        var buildPhase = new BuildPhase(agent);
        LintGate lintGate = agents.Lint(log);
        var testGate = new TestGate(agents.Host, log, request.SkipIntegration);
        var reviewPhase = new ReviewPhase(agents.Reviewer, git);

        int redos = 0;
        string producer;
        IReadOnlyList<string> redoTexts = [];
        while (true)
        {
            await Say("build: start").ConfigureAwait(false);
            long buildStart = _clock.GetTimestamp();
            BuildPhaseResult built = await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
            if (!built.Passed)
            {
                _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "failed");
                return await StopAsync(request, built.Failure, redos, cancellationToken).ConfigureAwait(false);
            }


            await Say("lint: start").ConfigureAwait(false);
            bool linted = await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
            await agents.AfterLintAsync(linted, cancellationToken).ConfigureAwait(false);
            _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "passed");
            long testStart = _clock.GetTimestamp();
            await Say("test: start").ConfigureAwait(false);
            TestGateResult gate = await testGate.RunAsync(request.Directory, plan.Pin, request.SecretNames, cancellationToken).ConfigureAwait(false);
            await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
            producer = _summary.AddRow("test", _clock.GetElapsedTime(testStart), gate.Passed ? "passed" : "failed");
            if (gate.Passed)
            {
                await Say("review: start").ConfigureAwait(false);
                ReviewPhaseResult review = await reviewPhase
                    .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                string reviewRow = _summary.AddRow("review", null, review.IsError ? "failed" : review.Approved ? "approved" : "blocked");
                if (review.IsError)
                {
                    return await StopAsync(request, review.Blocking[0], redos, cancellationToken).ConfigureAwait(false);
                }

                await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                if (review.Approved)
                {
                    await Say("deliver: start").ConfigureAwait(false);
                    var deliver = new Deliver(git, new PullRequests(client), client, Facts(agents, baseCommit, gate, false, redos));
                    PullRequest pullRequest = await deliver
                        .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                    return new FactoryRunResult(pullRequest, redos);
                }

                redoTexts = review.Blocking;
                producer = reviewRow;
            }
            else
            {
                redoTexts = gate.Evidence;
            }

            redoTexts = agents.NoteBuild(redoTexts);
            if (redos >= RedoBudget)
            {
                await CommentAsync(request, $"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, redos);
            }

            redos++;
            _summary.AddRedos(redos, producer, redoTexts);
            await Say($"redo: {redos}").ConfigureAwait(false);
        }
    }

    private async Task<FactoryRunResult> RunRefactorAsync(
        FactoryRunRequest request,
        GitHubIssue issue,
        RunAgents agents,
        Git git,
        string baseCommit,
        CancellationToken cancellationToken)
    {
        CommandHost host = agents.Host;
        Agent agent = agents.Builder;
        string? check = CleanupBody.ReadCheck(issue.Body);
        if (check is not null)
        {
            CommandResult first = await RunCheckAsync(host, check, cancellationToken).ConfigureAwait(false);
            await Say(first.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
            if (first.ExitCode is 0)
            {
                await CommentAsync(request, "The check passes before the plan, so there is nothing to refactor.", [], cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }

            if (first.ExitCode is 126 or 127)
            {
                await CommentAsync(request, $"The check command exited with the code {first.ExitCode} before the plan, so it did not run. Its standard error:", [first.StandardError], cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }
        }

        await Say("plan: start").ConfigureAwait(false);
        PlanPhaseResult plan = await new PlanPhase(agent, refactor: true)
            .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
        if (!plan.Passed)
        {
            _summary.AddRow("plan", null, "failed");
            await Say("plan: failed").ConfigureAwait(false);
            await CommentAsync(request, "The plan phase failed:", plan.Failures, cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        _summary.AddRow("plan", null, "passed");
        await Say("plan: passed").ConfigureAwait(false);
        var pin = Pin.TakeRefactor(request.Directory);
        var buildPhase = new BuildPhase(agent, refactor: true);
        LintGate lintGate = agents.Lint(log);
        var testGate = new TestGate(agents.Host, log, request.SkipIntegration);
        var reviewPhase = new ReviewPhase(agents.Reviewer, git, refactor: true);

        int redos = 0;
        string producer;
        IReadOnlyList<string> redoTexts = [];
        while (true)
        {
            await Say("build: start").ConfigureAwait(false);
            long buildStart = _clock.GetTimestamp();
            BuildPhaseResult built = await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
            if (!built.Passed)
            {
                _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "failed");
                return await StopAsync(request, built.Failure, redos, cancellationToken).ConfigureAwait(false);
            }


            await Say("lint: start").ConfigureAwait(false);
            bool linted = await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
            await agents.AfterLintAsync(linted, cancellationToken).ConfigureAwait(false);
            _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "passed");
            long testStart = _clock.GetTimestamp();
            await Say("test: start").ConfigureAwait(false);
            TestGateResult gate = await testGate.RunAsync(request.Directory, pin, request.SecretNames, cancellationToken).ConfigureAwait(false);
            await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
            producer = _summary.AddRow("test", _clock.GetElapsedTime(testStart), gate.Passed ? "passed" : "failed");
            if (!gate.Passed)
            {
                redoTexts = gate.Evidence;
            }
            else
            {
                CommandResult? after = null;
                if (check is not null)
                {
                    long checkStart = _clock.GetTimestamp();
                    after = await RunCheckAsync(host, check, cancellationToken).ConfigureAwait(false);
                    await Say(after.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
                    producer = _summary.AddRow("check", _clock.GetElapsedTime(checkStart), after.ExitCode is 0 ? "green" : "red");
                }

                if (after is not null && after.ExitCode is not 0)
                {
                    redoTexts = [CleanupGate.CheckRedo(after)];
                }
                else
                {
                    await Say("review: start").ConfigureAwait(false);
                    ReviewPhaseResult review = await reviewPhase
                        .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                    string reviewRow = _summary.AddRow("review", null, review.IsError ? "failed" : review.Approved ? "approved" : "blocked");
                    if (review.IsError)
                    {
                        return await StopAsync(request, review.Blocking[0], redos, cancellationToken).ConfigureAwait(false);
                    }

                    await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                    if (review.Approved)
                    {
                        await Say("deliver: start").ConfigureAwait(false);
                        var deliver = new Deliver(git, new PullRequests(client), client, Facts(agents, baseCommit, gate, check is not null, redos));
                        PullRequest pullRequest = await deliver
                            .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                        return new FactoryRunResult(pullRequest, redos);
                    }

                    redoTexts = review.Blocking;
                    producer = reviewRow;
                }
            }

            redoTexts = agents.NoteBuild(redoTexts);
            if (redos >= RedoBudget)
            {
                await CommentAsync(request, $"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, redos);
            }

            redos++;
            _summary.AddRedos(redos, producer, redoTexts);
            await Say($"redo: {redos}").ConfigureAwait(false);
        }
    }

    private async Task<FactoryRunResult> RunCleanupAsync(
        FactoryRunRequest request,
        GitHubIssue issue,
        IReadOnlyList<string> scope,
        string check,
        RunAgents agents,
        Git git,
        string baseCommit,
        CancellationToken cancellationToken)
    {
        CommandHost host = agents.Host;
        Agent agent = agents.Builder;
        CommandResult first = await RunCheckAsync(host, check, cancellationToken).ConfigureAwait(false);
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
        LintGate lintGate = agents.Lint(log);
        var testGate = new TestGate(agents.Host, log, request.SkipIntegration);
        var reviewPhase = new ReviewPhase(agents.Reviewer, git, cleanup: true);
        var pin = Pin.Take(request.Directory);
        IReadOnlySet<string> basePassed = await testGate.ReadPassedTestsAsync(request.Directory, cancellationToken).ConfigureAwait(false);

        int redos = 0;
        string producer = "";
        IReadOnlyList<string> redoTexts = [];
        while (true)
        {
            await Say("build: start").ConfigureAwait(false);
            long buildStart = _clock.GetTimestamp();
            BuildPhaseResult built = await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
            if (!built.Passed)
            {
                _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "failed");
                return await StopAsync(request, built.Failure, redos, cancellationToken).ConfigureAwait(false);
            }


            await Say("lint: start").ConfigureAwait(false);
            bool linted = await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
            await agents.AfterLintAsync(linted, cancellationToken).ConfigureAwait(false);
            producer = _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "passed");
            redoTexts = await CleanupGate.ScopeFaultsAsync(git, baseCommit, request.Directory, scope, cancellationToken).ConfigureAwait(false);
            if (redoTexts.Count is 0)
            {
                long testStart = _clock.GetTimestamp();
                await Say("test: start").ConfigureAwait(false);
                TestGateResult gate = await testGate
                    .RunAsync(request.Directory, pin, request.SecretNames, path => CleanupGate.PinExempt(scope, path), cancellationToken)
                    .ConfigureAwait(false);
                await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
                producer = _summary.AddRow("test", _clock.GetElapsedTime(testStart), gate.Passed ? "passed" : "failed");
                redoTexts = !gate.Passed ? gate.Evidence : CleanupGate.LostTests(basePassed, gate);

                if (redoTexts.Count is 0)
                {
                    long checkStart = _clock.GetTimestamp();
                    CommandResult after = await RunCheckAsync(host, check, cancellationToken).ConfigureAwait(false);
                    await Say(after.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
                    producer = _summary.AddRow("check", _clock.GetElapsedTime(checkStart), after.ExitCode is 0 ? "green" : "red");
                    if (after.ExitCode is 0)
                    {
                        await Say("review: start").ConfigureAwait(false);
                        ReviewPhaseResult review = await reviewPhase
                            .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                        string reviewRow = _summary.AddRow("review", null, review.IsError ? "failed" : review.Approved ? "approved" : "blocked");
                        if (review.IsError)
                        {
                            return await StopAsync(request, review.Blocking[0], redos, cancellationToken).ConfigureAwait(false);
                        }

                        await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                        if (review.Approved)
                        {
                            await Say("deliver: start").ConfigureAwait(false);
                            var deliver = new Deliver(git, new PullRequests(client), client, Facts(agents, baseCommit, gate, true, redos));
                            PullRequest pullRequest = await deliver
                                .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                            return new FactoryRunResult(pullRequest, redos);
                        }

                        redoTexts = review.Blocking;
                        producer = reviewRow;
                    }
                    else
                    {
                        redoTexts = [CleanupGate.CheckRedo(after)];
                    }
                }
            }

            redoTexts = agents.NoteBuild(redoTexts);
            if (redos >= RedoBudget)
            {
                await CommentAsync(request, $"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, redos);
            }

            redos++;
            _summary.AddRedos(redos, producer, redoTexts);
            await Say($"redo: {redos}").ConfigureAwait(false);
        }
    }

    private async Task<FactoryRunResult> RunReframeAsync(
        FactoryRunRequest request,
        GitHubIssue issue,
        IReadOnlyList<string> scope,
        string check,
        RunAgents agents,
        Git git,
        string baseCommit,
        CancellationToken cancellationToken)
    {
        CommandHost host = agents.Host;
        Agent agent = agents.Builder;
        CommandResult first = await RunCheckAsync(host, check, cancellationToken).ConfigureAwait(false);
        await Say(first.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
        if (first.ExitCode is 0)
        {
            await CommentAsync(request, "The check passes before the build, so there is nothing to reframe.", [], cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        if (first.ExitCode is 126 or 127)
        {
            await CommentAsync(request, $"The check command exited with the code {first.ExitCode} before the build, so it did not run. Its standard error:", [first.StandardError], cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        var buildPhase = new BuildPhase(agent, reframe: true);
        LintGate lintGate = agents.Lint(log);
        var testGate = new TestGate(agents.Host, log, request.SkipIntegration);
        var reviewPhase = new ReviewPhase(agents.Reviewer, git, reframe: true);
        var pin = Pin.Take(request.Directory);
        IReadOnlySet<string> basePassed = await testGate.ReadPassedTestsAsync(request.Directory, cancellationToken).ConfigureAwait(false);
        ApiListing baseApi = await ListApiAsync(host, cancellationToken).ConfigureAwait(false);
        if (baseApi.Lines is null)
        {
            await CommentAsync(request, "The public API of the base commit could not be listed, so there is no build:", [baseApi.Fault ?? ""], cancellationToken).ConfigureAwait(false);
            return new FactoryRunResult(null, 0);
        }

        await Say("api: listed").ConfigureAwait(false);

        int redos = 0;
        string producer = "";
        IReadOnlyList<string> redoTexts = [];
        while (true)
        {
            await Say("build: start").ConfigureAwait(false);
            long buildStart = _clock.GetTimestamp();
            BuildPhaseResult built = await buildPhase.RunAsync(issue.Title, issue.Body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
            if (!built.Passed)
            {
                _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "failed");
                return await StopAsync(request, built.Failure, redos, cancellationToken).ConfigureAwait(false);
            }

            await Say("lint: start").ConfigureAwait(false);
            bool linted = await lintGate.RunAsync(request.Directory, issue.Title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
            await agents.AfterLintAsync(linted, cancellationToken).ConfigureAwait(false);
            producer = _summary.AddRow("build", _clock.GetElapsedTime(buildStart), "passed");
            redoTexts = await CleanupGate.ScopeFaultsAsync(git, baseCommit, request.Directory, scope, cancellationToken).ConfigureAwait(false);
            TestGateResult? gate = null;
            if (redoTexts.Count is 0)
            {
                long testStart = _clock.GetTimestamp();
                await Say("test: start").ConfigureAwait(false);
                gate = await testGate
                    .RunAsync(request.Directory, pin, request.SecretNames, path => CleanupGate.PinExempt(scope, path), cancellationToken)
                    .ConfigureAwait(false);
                await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
                producer = _summary.AddRow("test", _clock.GetElapsedTime(testStart), gate.Passed ? "passed" : "failed");
                redoTexts = !gate.Passed ? gate.Evidence : CleanupGate.LostTests(basePassed, gate);
            }

            if (redoTexts.Count is 0)
            {
                long apiStart = _clock.GetTimestamp();
                ApiListing api = await ListApiAsync(host, cancellationToken).ConfigureAwait(false);
                redoTexts = api.Lines is null ? [api.Fault ?? ""] : ApiGate.Compare(baseApi.Lines, api.Lines);
                if (api.Lines is not null)
                {
                    await Say(redoTexts.Count is 0 ? "api: same" : "api: changed").ConfigureAwait(false);
                }

                producer = _summary.AddRow("api", _clock.GetElapsedTime(apiStart), redoTexts.Count is 0 ? "same" : "changed");
            }

            if (redoTexts.Count is 0 && gate is not null)
            {
                long checkStart = _clock.GetTimestamp();
                CommandResult after = await RunCheckAsync(host, check, cancellationToken).ConfigureAwait(false);
                await Say(after.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
                producer = _summary.AddRow("check", _clock.GetElapsedTime(checkStart), after.ExitCode is 0 ? "green" : "red");
                if (after.ExitCode is 0)
                {
                    await Say("review: start").ConfigureAwait(false);
                    ReviewPhaseResult review = await reviewPhase
                        .RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                    string reviewRow = _summary.AddRow("review", null, review.IsError ? "failed" : review.Approved ? "approved" : "blocked");
                    if (review.IsError)
                    {
                        return await StopAsync(request, review.Blocking[0], redos, cancellationToken).ConfigureAwait(false);
                    }

                    await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                    if (review.Approved)
                    {
                        await Say("deliver: start").ConfigureAwait(false);
                        var deliver = new Deliver(git, new PullRequests(client), client, Facts(agents, baseCommit, gate, true, redos));
                        PullRequest pullRequest = await deliver
                            .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                        return new FactoryRunResult(pullRequest, redos);
                    }

                    redoTexts = review.Blocking;
                    producer = reviewRow;
                }
                else
                {
                    redoTexts = [CleanupGate.CheckRedo(after)];
                }
            }

            redoTexts = agents.NoteBuild(redoTexts);
            if (redos >= RedoBudget)
            {
                await CommentAsync(request, $"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, redos);
            }

            redos++;
            _summary.AddRedos(redos, producer, redoTexts);
            await Say($"redo: {redos}").ConfigureAwait(false);
        }
    }

    private Task<ApiListing> ListApiAsync(CommandHost host, CancellationToken cancellationToken) =>
        ApiGate.ListAsync(host, progress, log, cancellationToken);

    private DeliveryFacts Facts(RunAgents agents, string baseCommit, TestGateResult gate, bool checkGreen, int redos) => new(
        baseCommit,
        gate.Tests?.Count(test => test.Outcome is TestOutcome.Passed) ?? 0,
        checkGreen,
        redos,
        _spend,
        (int)(_clock.GetElapsedTime(_started).Ticks / TimeSpan.TicksPerSecond),
        version,
        agents.Sessions);

    private async Task<FactoryRunResult> StopAsync(FactoryRunRequest request, string text, int redos, CancellationToken cancellationToken)
    {
        string shown = string.IsNullOrWhiteSpace(text) ? "claude answered with an error and no text." : text;
        await CommentAsync(request, shown, [], cancellationToken).ConfigureAwait(false);
        return new FactoryRunResult(null, redos);
    }

    private Task<CommandResult> RunCheckAsync(CommandHost host, string command, CancellationToken cancellationToken) =>
        CleanupGate.RunCheckAsync(host, command, progress, cancellationToken, log);

    private async Task WriteSummaryAsync()
    {
        if (string.IsNullOrEmpty(summaryPath) || _summary.IsEmpty)
        {
            return;
        }

        _summary.AddSessions(_sessions);

        try
        {
            await JobSummary.AppendToFileAsync(summaryPath, _summary.Render()).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await ProgressLine.WriteAsync(log, $"summary: cannot write {summaryPath}: {exception.Message}").ConfigureAwait(false);
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
