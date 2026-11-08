namespace Core;

/// <summary>What a <see cref="FactoryRun"/> needs: the issue, the clone, the models and the author. <paramref name="BaseCommit"/> is the
/// full SHA of the commit the run builds on, or null for the head of the default branch that the clone checks out.</summary>
public sealed record FactoryRunRequest(
    IssueNumber Issue,
    string Token,
    string RunId,
    string Directory,
    string BaseBranch,
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
    string? BaseCommit = null);

/// <summary>What a <see cref="FactoryRun"/> that passed gives: the pull request it opened, and the head commit it pushed to it.</summary>
public sealed record Delivered(PullRequest PullRequest, string Head);

/// <summary>Takes one issue of a target repo to a pull request within three Redos, with the agents of the backend that
/// <paramref name="startBackend"/> starts.</summary>
public sealed class FactoryRun(
    CommandRunner runner,
    GitHubClient client,
    StartBackend startBackend,
    TextWriter? progress = null,
    TextWriter? log = null,
    TimeProvider? time = null,
    string? summaryPath = null,
    string? version = null)
{
    /// <summary>The heading of the section of a factory run in the job summary.</summary>
    public const string SummaryHeading = "Factory run";

    private static readonly string[] LayoutFiles = ["Solution.slnx"];
    private static readonly string[] LayoutDirectories = ["specs/features/", "tests/Specs/", "tests/Integration/"];

    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private readonly Run _run = new(runner, client, startBackend, progress, log, time, summaryPath);

    /// <summary>Runs the issue through the plan, the build, the gates and the review, and delivers an approved build. An issue
    /// that the run declines, as an input error of its author, ends with a comment, no run and the end <see cref="RunEnd.Declined"/>.</summary>
    public async Task<RunResult<Delivered>> RunAsync(FactoryRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        long started = _clock.GetTimestamp();
        RepoName repo = request.Issue.Repo;
        GitHubIssue issue = await client.ReadIssueAsync(request.Issue, cancellationToken).ConfigureAwait(false);
        RecipeChoice choice = RecipeRules.Parse(issue.Labels, issue.Body);
        if (choice.Declined is string refusal)
        {
            string comment = await CommentAsync(request, refusal, [], cancellationToken).ConfigureAwait(false);
            return new RunResult<Delivered>(null, new RunStop(comment, RunEnd.Declined), 0);
        }

        var shared = new RunRequest(
            repo, request.Token, request.Directory, request.ReviewModel, request.ReviewTurns, request.AuthorName,
            request.AuthorEmail, request.SecretNames, request.SkipIntegration);
        return await _run.RunAsync(shared, new FactoryKind(this, request, issue, choice, started), cancellationToken).ConfigureAwait(false);
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    /// <summary>Runs the check of the issue before the first phase, and gives the text of the comment that stops the run, or null.</summary>
    private Task<string?> PreCheckAsync(Sandbox sandbox, string check, PreCheckWords words, CancellationToken cancellationToken) =>
        CheckGate.PreCheckAsync(sandbox, check, words, progress, log, cancellationToken);

    private DeliveryFacts Facts(string baseCommit, TestGate gate, RecipeChoice choice, int redos, decimal spend, long started, IReadOnlyList<SessionCost> sessions) => new(
        baseCommit,
        gate.Tests?.Count(test => test.Outcome is TestOutcome.Passed) ?? 0,
        choice.Check is not null,
        redos,
        spend,
        (int)(_clock.GetElapsedTime(started).Ticks / TimeSpan.TicksPerSecond),
        version,
        sessions);

    private Task<PullRequest> DeliverAsync(FactoryRunRequest request, string title, Git git, DeliveryFacts facts, CancellationToken cancellationToken) =>
        new Deliver(git, new PullRequests(client), client, facts)
            .RunAsync(request.Directory, request.RunId, request.Issue, request.BaseBranch, title, request.Token, cancellationToken);

    /// <summary>Posts the comment of <paramref name="lead"/> and <paramref name="texts"/> on the issue, and gives its body.</summary>
    private async Task<string> CommentAsync(FactoryRunRequest request, string lead, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        string body = CommentText.Fit(lead, texts);
        await PostAsync(request, body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    /// <summary>Writes <paramref name="body"/> as a progress line and posts it on the issue unchanged.</summary>
    private async Task PostAsync(FactoryRunRequest request, string body, CancellationToken cancellationToken)
    {
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Issue, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The factory run as a kind of <see cref="Run"/>. The checkout takes the base commit of the request, checks the layout and
    /// applies the scenarios of the issue. The step before the gates runs the check of the issue and the plan phase. A pass delivers
    /// the pull request and reads the head commit it pushed.
    /// </summary>
    private sealed class FactoryKind(FactoryRun owner, FactoryRunRequest request, GitHubIssue issue, RecipeChoice choice, long started) : IRunKind<Delivered>
    {
        private readonly RecipeSequence _sequence = RecipeRules.Sequence(choice, RunKind.Factory);
        private Git? _git;
        private string _baseCommit = "";
        private IssueScenariosResult? _scenarios;

        public RunKind Kind => RunKind.Factory;

        public RecipeChoice Choice => choice;

        public string Title => issue.Title;

        public string Body => issue.Body;

        public string ScratchBranch => $"factory/{request.RunId}-session";

        public int CommentNumber => request.Issue.Number;

        public string SummaryHeading => FactoryRun.SummaryHeading;

        public async Task<BaseOrStop> CheckoutAsync(Git git, CancellationToken cancellationToken = default)
        {
            _git = git;
            if (request.BaseCommit is string chosen)
            {
                await git.CheckoutCommitAsync(chosen, request.Directory, cancellationToken).ConfigureAwait(false);
            }

            _baseCommit = await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false);

            List<string> absent = [];
            absent.AddRange(LayoutFiles.Where(path => !File.Exists(Path.Combine(request.Directory, path))));
            absent.AddRange(LayoutDirectories.Where(path => !Directory.Exists(Path.Combine(request.Directory, path))));
            if (absent.Count > 0)
            {
                await owner.Say("layout: failed").ConfigureAwait(false);
                string comment = await owner.CommentAsync(request, "The clone lacks these paths of the layout:", absent, cancellationToken).ConfigureAwait(false);
                return new BaseOrStop(null, new RunStop(comment, RunEnd.Stopped));
            }

            if (_sequence.Plan is { Input: PlanInput.IssueScenarios } && IssueScenarios.Holds(issue.Body))
            {
                _scenarios = IssueScenarios.Apply(issue.Body, request.Directory);
                if (_scenarios.Faults.Count > 0)
                {
                    string comment = await owner.CommentAsync(request, "The scenarios of the issue are not sound, so there is no run:", _scenarios.Faults, cancellationToken).ConfigureAwait(false);
                    await owner.Say("scenarios: failed").ConfigureAwait(false);
                    return new BaseOrStop(null, new RunStop(comment, RunEnd.Declined));
                }
            }

            return new BaseOrStop(_baseCommit, null);
        }

        public async Task<PinOrStop> BeforeGatesAsync(AgentBackend backend, JobSummary summary, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(summary);
            if (_sequence.PreCheck is PreCheckWords words)
            {
                string check = choice.Check ?? throw new InvalidOperationException("A sequence with a check before the first phase comes from a choice with a check.");
                if (await owner.PreCheckAsync(backend.Sandbox, check, words, cancellationToken).ConfigureAwait(false) is string stop)
                {
                    return new PinOrStop(null, stop);
                }
            }

            if (_sequence.Plan is not PlanStep step)
            {
                return new PinOrStop(RecipeRules.TakePin(_sequence.Pin, request.Directory), null);
            }

            await owner.Say("plan: start").ConfigureAwait(false);
            PlanPhaseResult plan = await new PlanPhase(backend.Builder, RecipeRules.PlanText(choice.Recipe, step, _scenarios))
                .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
            if (!plan.Passed || (_sequence.Pin is PinSource.Plan && plan.Pin is null))
            {
                await summary.AddRowAsync(Phase.Plan.Text(), null, "failed", cancellationToken).ConfigureAwait(false);
                return new PinOrStop(null, CommentText.Fit("The plan phase failed:", plan.Failures));
            }

            await summary.AddRowAsync(Phase.Plan.Text(), null, "passed", cancellationToken).ConfigureAwait(false);
            return new PinOrStop(plan.Pin is Pin planned && _sequence.Pin is PinSource.Plan ? planned : RecipeRules.TakePin(_sequence.Pin, request.Directory), null);
        }

        public IBuild Build(Agent builder) => new FactoryBuild(
            new BuildPhase(builder, RecipeRules.BuildText(choice.Recipe, RunKind.Factory)), issue.Title, issue.Body, request.BuildModel, request.BuildTurns);

        public async Task<Delivered> AfterPassAsync(AgentBackend backend, GateSet gates, BuildLoopResult result, decimal spend, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(gates);
            ArgumentNullException.ThrowIfNull(result);
            Git git = _git ?? throw new InvalidOperationException("The run delivers only after its checkout.");
            await owner.Say("deliver: start").ConfigureAwait(false);
            TestGate testGate = gates.Gates.OfType<TestGate>().Single();
            DeliveryFacts facts = owner.Facts(_baseCommit, testGate, choice, result.Redos, spend, started, backend.Sessions);
            PullRequest pullRequest = await owner.DeliverAsync(request, issue.Title, git, facts, cancellationToken).ConfigureAwait(false);
            return new Delivered(pullRequest, await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false));
        }
    }
}
