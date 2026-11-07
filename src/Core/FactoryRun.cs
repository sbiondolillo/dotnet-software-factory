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

    private static readonly string[] LayoutFiles = ["Solution.slnx"];
    private static readonly string[] LayoutDirectories = ["specs/features/", "tests/Specs/", "tests/Integration/"];

    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private readonly Run _run = new(runner, client, progress, log, time, summaryPath, handler, delay);

    /// <summary>True when the last <see cref="RunAsync"/> ended on an input error of the issue's author, with a comment and no run.</summary>
    public bool Declined { get; private set; }

    /// <summary>Runs the issue through the plan, the build, the gates and the review, and delivers an approved build.</summary>
    public async Task<FactoryRunResult> RunAsync(FactoryRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Declined = false;
        long started = _clock.GetTimestamp();
        GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, request.IssueNumber, cancellationToken).ConfigureAwait(false);
        RecipeChoice choice = RecipeRules.Parse(issue.Labels, issue.Body);
        if (choice.Declined is string refusal)
        {
            await CommentAsync(request, refusal, [], cancellationToken).ConfigureAwait(false);
            Declined = true;
            return new FactoryRunResult(null, 0);
        }

        var shared = new RunRequest(
            request.Owner, request.Repo, request.Token, request.Directory, request.ImageTag, request.RootDirectory, request.ReviewModel,
            request.ReviewTurns, request.AuthorName, request.AuthorEmail, request.SecretNames, request.SkipIntegration, request.Managed);
        RunOutcome<PullRequest> outcome = await _run.RunAsync(shared, new FactoryKind(this, request, issue, choice, started), cancellationToken).ConfigureAwait(false);
        return new FactoryRunResult(outcome.Passed, outcome.Redos);
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    /// <summary>Runs the check of the issue before the first phase, and gives the text of the comment that stops the run, or null.</summary>
    private Task<string?> PreCheckAsync(Sandbox sandbox, string check, Recipe recipe, CancellationToken cancellationToken) =>
        CheckGate.PreCheckAsync(sandbox, check, recipe, progress, log, cancellationToken);

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
            .RunAsync(request.Directory, request.RunId, request.Owner, request.Repo, request.BaseBranch, request.IssueNumber, title, request.Token, cancellationToken);

    private Task CommentAsync(FactoryRunRequest request, string lead, IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        PostAsync(request, CommentText.Fit(lead, texts), cancellationToken);

    /// <summary>Writes <paramref name="body"/> as a progress line and posts it on the issue unchanged.</summary>
    private async Task PostAsync(FactoryRunRequest request, string body, CancellationToken cancellationToken)
    {
        await Say(body).ConfigureAwait(false);
        await client.PostCommentAsync(request.Owner, request.Repo, request.IssueNumber, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The factory run as a kind of <see cref="Run"/>. The checkout takes the base commit of the request, checks the layout and
    /// applies the scenarios of the issue. The step before the gates runs the check of the issue and the plan phase. A pass delivers
    /// the pull request.
    /// </summary>
    private sealed class FactoryKind(FactoryRun owner, FactoryRunRequest request, GitHubIssue issue, RecipeChoice choice, long started) : IRunKind<PullRequest>
    {
        private Git? _git;
        private string _baseCommit = "";
        private IssueScenariosResult? _scenarios;

        public RunKind Kind => RunKind.Factory;

        public RecipeChoice Choice => choice;

        public string Title => issue.Title;

        public string Body => issue.Body;

        public string ScratchBranch => $"factory/{request.RunId}-session";

        public int CommentNumber => request.IssueNumber;

        public string SummaryHeading => FactoryRun.SummaryHeading;

        public async Task<string?> CheckoutAsync(Git git, CancellationToken cancellationToken = default)
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
                await owner.CommentAsync(request, "The clone lacks these paths of the layout:", absent, cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (choice.Recipe is Recipe.Behavior && IssueScenarios.Holds(issue.Body))
            {
                _scenarios = IssueScenarios.Apply(issue.Body, request.Directory);
                if (_scenarios.Faults.Count > 0)
                {
                    await owner.CommentAsync(request, "The scenarios of the issue are not sound, so there is no run:", _scenarios.Faults, cancellationToken).ConfigureAwait(false);
                    await owner.Say("scenarios: failed").ConfigureAwait(false);
                    owner.Declined = true;
                    return null;
                }
            }

            return _baseCommit;
        }

        public async Task<PinOrStop> BeforeGatesAsync(RunAgents agents, JobSummary summary, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(agents);
            ArgumentNullException.ThrowIfNull(summary);
            if (choice.Check is string check && await owner.PreCheckAsync(agents.Sandbox, check, choice.Recipe, cancellationToken).ConfigureAwait(false) is string stop)
            {
                return new PinOrStop(null, stop);
            }

            if (choice.Recipe is not (Recipe.Behavior or Recipe.Refactor))
            {
                return new PinOrStop(RecipeRules.TakePin(choice.Recipe, request.Directory), null);
            }

            await owner.Say("plan: start").ConfigureAwait(false);
            PlanPhaseResult plan = await new PlanPhase(agents.Builder, RecipeRules.PlanText(choice.Recipe, _scenarios))
                .RunAsync(issue.Title, issue.Body, request.PlanModel, request.PlanTurns, request.Directory, cancellationToken).ConfigureAwait(false);
            if (!plan.Passed || (choice.Recipe is Recipe.Behavior && plan.Pin is null))
            {
                await summary.AddRowAsync(Phase.Plan.Text(), null, "failed", cancellationToken).ConfigureAwait(false);
                return new PinOrStop(null, CommentText.Fit("The plan phase failed:", plan.Failures));
            }

            await summary.AddRowAsync(Phase.Plan.Text(), null, "passed", cancellationToken).ConfigureAwait(false);
            return new PinOrStop(plan.Pin is Pin planned && choice.Recipe is Recipe.Behavior ? planned : RecipeRules.TakePin(choice.Recipe, request.Directory), null);
        }

        public IBuild Build(Agent builder) => new FactoryBuild(
            new BuildPhase(builder, RecipeRules.BuildText(choice.Recipe, RunKind.Factory)), issue.Title, issue.Body, request.BuildModel, request.BuildTurns);

        public async Task<PullRequest> AfterPassAsync(RunAgents agents, GateSet gates, BuildLoopResult result, decimal spend, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(agents);
            ArgumentNullException.ThrowIfNull(gates);
            ArgumentNullException.ThrowIfNull(result);
            Git git = _git ?? throw new InvalidOperationException("The run delivers only after its checkout.");
            await owner.Say("deliver: start").ConfigureAwait(false);
            TestGate testGate = gates.Gates.OfType<TestGate>().Single();
            DeliveryFacts facts = owner.Facts(_baseCommit, testGate, choice, result.Redos, spend, started, agents.Sessions);
            return await owner.DeliverAsync(request, issue.Title, git, facts, cancellationToken).ConfigureAwait(false);
        }
    }
}
