namespace Core;

/// <summary>What a <see cref="FactoryRun"/> needs: the issue, the clone, the models and the author. <paramref name="BaseCommit"/> is the
/// full SHA of the commit the run builds on, or null for the head of the default branch that the clone checks out.</summary>
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
    ManagedAgentsSettings? Managed = null,
    string? BaseCommit = null);

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
    /// <summary>The heading of the section of a factory run in the job summary.</summary>
    public const string SummaryHeading = "Factory run";

    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private JobSummary _summary = new();
    private decimal _spend;
    private long _started;
    private IReadOnlyList<SessionCost> _sessions = [];

    private static readonly string[] LayoutFiles = ["Solution.slnx"];
    private static readonly string[] LayoutDirectories = ["specs/features/", "tests/Specs/", "tests/Integration/"];

    /// <summary>True when the last <see cref="RunAsync"/> ended on an input error of the issue's author, with a comment and no run.</summary>
    public bool Declined { get; private set; }

    /// <summary>Runs the issue through the plan, the build, the gates and the review, and delivers an approved build.</summary>
    public async Task<FactoryRunResult> RunAsync(FactoryRunRequest request, CancellationToken cancellationToken = default)
    {
        Declined = false;
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
        RecipeChoice choice = RecipeRules.Parse(issue.Labels, issue.Body);
        if (choice.Declined is string refusal)
        {
            await CommentAsync(request, refusal, [], cancellationToken).ConfigureAwait(false);
            Declined = true;
            return new FactoryRunResult(null, 0);
        }

        var git = new Git(runner);
        await Say("clone: start").ConfigureAwait(false);
        await git.CloneAsync($"https://github.com/{request.Owner}/{request.Repo}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
        if (request.BaseCommit is string chosen)
        {
            await git.CheckoutCommitAsync(chosen, request.Directory, cancellationToken).ConfigureAwait(false);
        }

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
        if (choice.Recipe is Recipe.Behavior && IssueScenarios.Holds(issue.Body))
        {
            scenarios = IssueScenarios.Apply(issue.Body, request.Directory);
            if (scenarios.Faults.Count > 0)
            {
                await CommentAsync(request, "The scenarios of the issue are not sound, so there is no run:", scenarios.Faults, cancellationToken).ConfigureAwait(false);
                await Say("scenarios: failed").ConfigureAwait(false);
                Declined = true;
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
                if (choice.Check is string check && await PreCheckAsync(request, agents.Sandbox, check, choice.Recipe, cancellationToken).ConfigureAwait(false))
                {
                    return new FactoryRunResult(null, 0);
                }

                Pin pin;
                if (choice.Recipe is Recipe.Behavior or Recipe.Refactor)
                {
                    await Say("plan: start").ConfigureAwait(false);
                    PlanPhaseResult plan = await new PlanPhase(agents.Builder, RecipeRules.PlanText(choice.Recipe, scenarios))
                        .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
                    if (!plan.Passed || (choice.Recipe is Recipe.Behavior && plan.Pin is null))
                    {
                        _summary.AddRow("plan", null, "failed");
                        await Say("plan: failed").ConfigureAwait(false);
                        await CommentAsync(request, "The plan phase failed:", plan.Failures, cancellationToken).ConfigureAwait(false);
                        return new FactoryRunResult(null, 0);
                    }

                    _summary.AddRow("plan", null, "passed");
                    await Say("plan: passed").ConfigureAwait(false);
                    pin = plan.Pin is Pin planned && choice.Recipe is Recipe.Behavior ? planned : RecipeRules.TakePin(choice.Recipe, request.Directory);
                }
                else
                {
                    pin = RecipeRules.TakePin(choice.Recipe, request.Directory);
                }

                GateSet gates = await new GateFactory(agents.Sandbox, git, request.Directory, baseCommit, request.SecretNames, progress, log, request.SkipIntegration)
                    .CreateAsync(choice, pin, cancellationToken).ConfigureAwait(false);
                if (gates.Stop is string stop)
                {
                    await CommentAsync(request, stop, [], cancellationToken).ConfigureAwait(false);
                    return new FactoryRunResult(null, 0);
                }

                var build = new FactoryBuild(
                    new BuildPhase(agents.Builder, RecipeRules.BuildText(choice.Recipe, RunKind.Factory)), issue.Title, issue.Body, request.BuildModel, request.BuildTurns);
                var reviewPhase = new ReviewPhase(agents.Reviewer, git, RecipeRules.ReviewSteps(choice.Recipe, RunKind.Factory));
                var loop = new BuildLoop(_summary, _clock, progress, agents, agents.Lint(log), request.Directory, issue.Title, request.AuthorName, request.AuthorEmail);
                BuildLoopResult result = await loop.RunAsync(
                    build,
                    gates.Gates,
                    token => reviewPhase.RunAsync(issue.Title, issue.Body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, token),
                    cancellationToken).ConfigureAwait(false);
                if (result.End is not BuildLoopEnd.Passed)
                {
                    await PostAsync(request, result.Comment ?? "", cancellationToken).ConfigureAwait(false);
                    return new FactoryRunResult(null, result.Redos);
                }

                await Say("deliver: start").ConfigureAwait(false);
                TestGate testGate = gates.Gates.OfType<TestGate>().Single();
                var deliver = new Deliver(git, new PullRequests(client), client, Facts(agents, baseCommit, testGate, choice, result.Redos));
                PullRequest pullRequest = await deliver
                    .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, issue.Title, request.Token, cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(pullRequest, result.Redos);
            }
            catch (SessionBranchException exception)
            {
                await CommentAsync(request, exception.Message, [], cancellationToken).ConfigureAwait(false);
                return new FactoryRunResult(null, 0);
            }
            finally
            {
                _sessions = agents.Sessions;
            }
        }
    }

    /// <summary>Runs the check before the first phase, and posts the comment that stops the run when there is one.</summary>
    private async Task<bool> PreCheckAsync(FactoryRunRequest request, Sandbox sandbox, string check, Recipe recipe, CancellationToken cancellationToken)
    {
        if (await CheckGate.PreCheckAsync(sandbox, check, recipe, progress, log, cancellationToken).ConfigureAwait(false) is not string stop)
        {
            return false;
        }

        await CommentAsync(request, stop, [], cancellationToken).ConfigureAwait(false);
        return true;
    }

    private DeliveryFacts Facts(RunAgents agents, string baseCommit, TestGate gate, RecipeChoice choice, int redos) => new(
        baseCommit,
        gate.Tests?.Count(test => test.Outcome is TestOutcome.Passed) ?? 0,
        choice.Check is not null,
        redos,
        _spend,
        (int)(_clock.GetElapsedTime(_started).Ticks / TimeSpan.TicksPerSecond),
        version,
        agents.Sessions);

    private async Task WriteSummaryAsync()
    {
        if (string.IsNullOrEmpty(summaryPath) || _summary.IsEmpty)
        {
            return;
        }

        _summary.AddSessions(_sessions);

        try
        {
            await JobSummary.AppendToFileAsync(summaryPath, _summary.Render(SummaryHeading)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await ProgressLine.WriteAsync(log, $"summary: cannot write {summaryPath}: {exception.Message}").ConfigureAwait(false);
        }
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    private Task CommentAsync(FactoryRunRequest request, string lead, IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        PostAsync(request, CommentText.Fit(lead, texts), cancellationToken);

    /// <summary>Writes <paramref name="body"/> as a progress line and posts it on the issue unchanged.</summary>
    private async Task PostAsync(FactoryRunRequest request, string body, CancellationToken cancellationToken)
    {
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Owner, request.Repo, request.IssueNumber, body, cancellationToken).ConfigureAwait(false);
    }
}
