using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>One request GitHub got.</summary>
public sealed record SeenRequest(string Method, string Uri, string? Content);

/// <summary>A clock that moves only when a stub advances it, in milliseconds.</summary>
public sealed class StepClock : TimeProvider
{
    private long _milliseconds;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _milliseconds;

    public void Advance(double seconds) => _milliseconds += (long)(seconds * 1000);
}

/// <summary>
/// A whole run, of either kind, over a <see cref="FakeBackend"/>: a real clone directory, a mock <see cref="CommandRunner"/> that
/// answers git and the docker commands of the gates, a stub GitHub with its request log, and the two <see cref="FakeAgent"/>s.
/// <see cref="RunFactoryAsync"/> runs a <see cref="FactoryRun"/> and <see cref="RunFeedbackAsync"/> a <see cref="FeedbackRun"/>.
/// </summary>
/// <remarks>
/// The agents start with the scripts of an agent that does its work: a plan prompt writes the plan, and the feature file
/// <c>specs/features/checkout.feature</c> unless the recipe plans alone; a build prompt writes the report that marks the threads A
/// and B fixed; a review prompt approves. A test changes a script on <see cref="Builder"/> or <see cref="Reviewer"/>, by phase and
/// by prompt number. The knobs on the harness drive git, the gates and GitHub. A gate knob counts the builds that ran before it,
/// so the build 0 is the base commit. The unit of these tests is the run: what an adapter does below the seam has its own tests.
/// </remarks>
public sealed class RunHarness : IDisposable
{
    public const string RedText = "Assert.Equal() Failure: Values differ";
    public const string ThreadA = "PRRT_kwDOUpm1Qc6ofWu5";
    public const string ThreadB = "PRRT_kwDOUpm1Qc6ofWvu";
    public const string ThreadC = "PRRT_kwDOUpm1Qc6ofWxx";
    public const string ReplyA = "The total now adds every item of the cart.";
    public const string ReplyB = "The title of the test names the unit.";
    public const string Approved = """{"approved":true,"blocking":[]}""";
    private const string Check = "grep -q Install README.md";

    private readonly DirectoryInfo _clone = Directory.CreateTempSubdirectory("factory-run-clone-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-run-root-");
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("factory-run-file-");
    private RunKind _kind;

    public RunHarness()
    {
        SummaryPath = Path.Combine(_files.FullName, "summary.md");
        Builder = new FakeAgent(_clone.FullName) { OnRequest = Seen };
        Reviewer = new FakeAgent(_clone.FullName) { OnRequest = Seen };
        Builder.Stage(Phase.Plan, "plan.md", PlanPhaseHarness.PlanText);
        Builder.Act(Phase.Plan, _ =>
        {
            if (!PlansAlone)
            {
                Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
            }
        });
        Builder.Stage(Phase.Build, "threads.json", FixedReport(ThreadA, ThreadB));
        Reviewer.Stage(Phase.Review, "review.json", Approved);
    }

    /// <summary>The agent of the plan and the build prompts.</summary>
    public FakeAgent Builder { get; }

    /// <summary>The agent of the review prompts.</summary>
    public FakeAgent Reviewer { get; }

    /// <summary>Each request of either agent, in order.</summary>
    public List<AgentRequest> AgentRequests { get; } = [];

    /// <summary>The number of build prompts so far.</summary>
    public int Builds => Builder.Of(Phase.Build).Count();

    public string Owner { get; set; } = "sbiondolillo";

    public string Repo { get; set; } = "factory-scratch";

    public string Token { get; set; } = "token";

    public List<string> SecretNames { get; set; } = [];

    /// <summary>True when the test gates of the run skip the integration suite.</summary>
    public bool SkipIntegration { get; set; }

    public string PlanModel { get; set; } = "plan-model";

    public int PlanTurns { get; set; } = 20;

    public string BuildModel { get; set; } = "build-model";

    public int BuildTurns { get; set; } = 60;

    public string ReviewModel { get; set; } = "review-model";

    public int ReviewTurns { get; set; } = 30;

    public string AuthorName { get; set; } = "Factory";

    public string AuthorEmail { get; set; } = "factory@example.com";

    /// <summary>The version that a factory run states in its summary and its pull request, or null for none.</summary>
    public string? Version { get; set; }

    // The issue: the issue of a factory run, and the closing issue of a feedback run.

    public int Issue { get; set; } = 12;

    public string Title { get; set; } = "Add a checkout total";

    public string Body { get; set; } = "The checkout shows the total of the cart.";

    /// <summary>The labels of the issue.</summary>
    public List<string> Labels { get; } = [];

    /// <summary>The status code of the read of the issue.</summary>
    public int IssueStatus { get; set; } = 200;

    // The pull request: the one a factory run opens, and the one a feedback run reads.

    public string RunId { get; set; } = "a1b2c3d4";

    public string BaseBranch { get; set; } = "main";

    /// <summary>The base commit the caller gives a factory run, or null for the head of the clone.</summary>
    public string? BaseCommit { get; set; }

    public int PullNumber { get; set; } = 10;

    public string PullUrl { get; set; } = "https://github.com/sbiondolillo/factory-scratch/pull/10";

    /// <summary>The status code of the opening of the pull request.</summary>
    public int PullStatus { get; set; } = 201;

    /// <summary>The pull request of a feedback run.</summary>
    public int Pull { get; set; } = 10;

    public string HeadRef { get; set; } = "factory/a1b2c3d4";

    /// <summary>The status code of the read of the pull request of a feedback run.</summary>
    public int HeadStatus { get; set; } = 200;

    /// <summary>The body of the pull request of a feedback run, or null for an answer with no body.</summary>
    public string? PullBody { get; set; }

    /// <summary>The ids of the threads GitHub lists, in order: A, B and C (resolved) are known.</summary>
    public List<string> Threads { get; set; } = [ThreadA, ThreadB];

    /// <summary>True when the thread A has no line.</summary>
    public bool NullLine { get; set; }

    /// <summary>The thread whose reply GitHub answers with a list of errors, or null for none.</summary>
    public string? ReplyErrorThread { get; set; }

    // Git and the clone.

    public string HeadSha { get; set; } = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";

    /// <summary>The paths of the layout that the clone lacks.</summary>
    public HashSet<string> Lacks { get; } = [];

    /// <summary>The files the clone holds besides the layout, each with the text "as on the base commit".</summary>
    public List<string> CloneFiles { get; } = [];

    public Action<string>? AfterClone { get; set; }

    public bool CloneFails { get; set; }

    /// <summary>The standard error of a <c>git checkout</c> that fails, or null for none.</summary>
    public string? CheckoutFailure { get; set; }

    /// <summary>The standard error of a <c>git push</c> that fails, or null for none.</summary>
    public string? PushFailure { get; set; }

    /// <summary>True when git reports a change in the work tree after a build.</summary>
    public bool ChangeAfterBuild { get; set; } = true;

    /// <summary>The files that git reports changed since the base after the first build.</summary>
    public List<string> FirstBuildChanges { get; } = [];

    /// <summary>The feature files git shows at the base commit, by path; a path with no entry is absent there.</summary>
    public Dictionary<string, string> BaseFiles { get; } = [];

    /// <summary>The paths that git reports changed since the base at every read, besides <see cref="FirstBuildChanges"/>.</summary>
    public List<string> ChangedPaths { get; } = [];

    // The gates.

    /// <summary>The builds after which the test gate is red.</summary>
    public HashSet<int> RedBuilds { get; } = [];

    /// <summary>The second Redo text of a red build, by build: a text that starts with "failed test: " names the failed test,
    /// and any other text is the standard output of the test command.</summary>
    public Dictionary<int, string> RedTexts { get; } = [];

    public string? TestStdout { get; set; }

    public bool TestCommandThrows { get; set; }

    /// <summary>What the check command answers after the build of that number (0 is before the first build). A number with no entry
    /// answers red before the first build of a factory run, and green after it and in a feedback run.</summary>
    public Dictionary<int, (int Code, string Out, string Err)> CheckResults { get; } = [];

    /// <summary>The passed tests the TRX file holds besides the first one.</summary>
    public int ExtraPassedTests { get; set; }

    /// <summary>Tests that the TRX file holds before the first build only, as class and method.</summary>
    public List<(string Class, string Method)> BaseOnlyTests { get; } = [];

    /// <summary>Tests that the TRX file holds after a build only, as class and method.</summary>
    public List<(string Class, string Method)> AfterOnlyTests { get; } = [];

    /// <summary>The lines the API printer prints on the base commit.</summary>
    public List<string> BaseApi { get; } = ["Core: Core.Greeter::method public static System.String Greet(System.String)", "Core: class static Core.Greeter"];

    /// <summary>The lines the API printer prints after a build, or null for the lines of the base commit.</summary>
    public List<string>? AfterApi { get; set; }

    /// <summary>The text the API printer prints after the build of that number, in place of <see cref="BaseApi"/> and
    /// <see cref="AfterApi"/>, or null for those.</summary>
    public Func<int, string>? ApiLines { get; set; }

    /// <summary>What the API printer answers after the build of that number (0 is before the first build) instead of a listing: its exit code and standard error.</summary>
    public Dictionary<int, (int Code, string Err)> ApiResults { get; } = [];

    /// <summary>True when the API printer exits with the code 1 after every build.</summary>
    public bool ApiFails { get; set; }

    /// <summary>What <c>dotnet build</c> in a copy of the head commit answers after the build of that number (0 is before the first build).</summary>
    public Dictionary<int, (int Code, string Out)> CopyBuildResults { get; } = [];

    /// <summary>True when <c>git worktree add</c> in the container exits with the code 128.</summary>
    public bool WorktreeAddFails { get; set; }

    /// <summary>The seconds the lint gate takes after the build of that number.</summary>
    public Dictionary<int, double> LintSeconds { get; } = [];

    /// <summary>The seconds the test gate takes after the build of that number.</summary>
    public Dictionary<int, double> TestSeconds { get; } = [];

    public bool LintFails { get; set; }

    // The backend.

    /// <summary>The sessions the fake backend gives the job summary.</summary>
    public List<SessionCost> Sessions { get; } = [];

    /// <summary>What the run handed the fake backend at its start, or null before the start.</summary>
    public BackendStart? Started { get; private set; }

    // What the run did.

    public string CloneDirectory => _clone.FullName;

    public string FilesDirectory => _files.FullName;

    /// <summary>The file that the run names as its job summary, or null for none.</summary>
    public string? SummaryPath { get; set; }

    /// <summary>The text of the job summary, or the empty string when the run wrote none.</summary>
    public string Summary => File.Exists(SummaryPath ?? "") ? File.ReadAllText(SummaryPath!).Replace("\r\n", "\n", StringComparison.Ordinal) : "";

    /// <summary>What the run wrote to its progress writer.</summary>
    public StringWriter Progress { get; } = new();

    /// <summary>The log of the run.</summary>
    public StringWriter Log { get; } = new();

    /// <summary>The clock of the run, which an act or a gate moves.</summary>
    public StepClock Clock { get; } = new();

    public List<CommandSpec> Commands { get; } = [];

    /// <summary>Each git command, as its words, each command in the container, as <c>exec</c> and its words, each prompt, as
    /// <c>prompt</c> and its phase, and each collect step of the backend, as <c>collect</c> and the branch, in order.</summary>
    public List<string> Timeline { get; } = [];

    /// <summary>The order in which pushes, comments, prompts, test commands, replies, resolves and container removals happened.</summary>
    public List<string> Events { get; } = [];

    public List<SeenRequest> Requests { get; } = [];

    public IEnumerable<SeenRequest> Comments =>
        Requests.Where(r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal));

    public IEnumerable<string> CommentBodies =>
        Comments.Select(c => JsonDocument.Parse(c.Content!).RootElement.GetProperty("body").GetString() ?? "");

    /// <summary>The body of the pull request the factory run opened, or null for none.</summary>
    public string? OpenedBody => Requests
        .Where(r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal))
        .Select(r => JsonDocument.Parse(r.Content!).RootElement.GetProperty("body").GetString())
        .LastOrDefault();

    /// <summary>True when the last run declined its issue.</summary>
    public bool Declined { get; private set; }

    /// <summary>The body of an issue with the scope and the check of a cleanup run.</summary>
    public static string CleanupBody => $"## Scope\nREADME.md\n\n## Check\n\n```sh\n{Check}\n```\n";

    /// <summary>True for a recipe whose plan prompt asks for the plan alone: a refactor, or an issue with scenarios or with
    /// removed scenarios, as <see cref="RecipeRules.PlanText"/> states.</summary>
    private bool PlansAlone =>
        Labels.Contains("refactor")
        || Body.Contains("## Scenarios", StringComparison.Ordinal)
        || Body.Contains("## Removed scenarios", StringComparison.Ordinal);

    public static string FixedReport(params string[] ids) => Items(ids.Select(id => (id, "fixed", id == ThreadA ? ReplyA : ReplyB)));

    public static string Items(IEnumerable<(string Id, string Status, string Reply)> items) =>
        JsonSerializer.Serialize(new { threads = items.Select(i => new { id = i.Id, status = i.Status, reply = i.Reply }) });

    /// <summary>Writes a file of the agent into the clone.</summary>
    public void Write(string relative, string text)
    {
        string full = Path.Combine(_clone.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>Removes a file of the clone, as the agent does.</summary>
    public void Remove(string relative) =>
        File.Delete(Path.Combine(_clone.FullName, relative));

    /// <summary>Appends <paramref name="text"/> to a file of the clone, as the agent does.</summary>
    public void Append(string relative, string text) =>
        Write(relative, File.ReadAllText(Path.Combine(_clone.FullName, relative)) + text);

    /// <summary>Scripts the build <paramref name="build"/> to write each of <paramref name="paths"/>.</summary>
    public void BuildWrites(int build, params string[] paths) =>
        Builder.Act(Phase.Build, _ => Array.ForEach(paths, path => Write(path, "written by the build")), build);

    /// <summary>Scripts the build <paramref name="build"/> to change each of <paramref name="paths"/>, which the clone holds.</summary>
    public void BuildChanges(int build, params string[] paths) =>
        Builder.Act(Phase.Build, _ => Array.ForEach(paths, path => Append(path, "\nchanged by the build")), build);

    /// <summary>Scripts the build <paramref name="build"/> to remove each of <paramref name="paths"/>.</summary>
    public void BuildRemoves(int build, params string[] paths) =>
        Builder.Act(Phase.Build, _ => Array.ForEach(paths, Remove), build);

    /// <summary>Scripts the report that the build <paramref name="build"/> writes, or every build for null. A null
    /// <paramref name="text"/> writes no report.</summary>
    public void Report(string? text, int? build = null) => Builder.Stage(Phase.Build, "threads.json", text, build);

    /// <summary>Scripts the answer of each prompt of <paramref name="phase"/> on the agent of that phase to take
    /// <paramref name="seconds"/> on the run's clock.</summary>
    private void Takes(Phase phase, double seconds, int? number = null) =>
        AgentOf(phase).Act(phase, _ => Clock.Advance(seconds), number);

    /// <summary>Scripts the prompt <paramref name="number"/> of <paramref name="phase"/> to answer with <paramref name="turns"/>,
    /// the cost <paramref name="cost"/> in dollars as written, and <paramref name="text"/>, after <paramref name="seconds"/> on the
    /// run's clock. An answer with the error flag writes no file, as an agent that failed writes none.</summary>
    public void Timed(Phase phase, int number, int turns, string cost, double seconds, bool isError = false, string text = "done")
    {
        FakeAgent agent = AgentOf(phase);
        if (isError)
        {
            agent.Clear(phase, number);
        }

        agent.Answer(phase, new AgentAnswer(text, turns, decimal.Parse(cost, CultureInfo.InvariantCulture), isError, new Dictionary<string, string>(), []), number);
        Takes(phase, seconds, number);
    }

    /// <summary>The reviewer for a review, and the builder for a plan or a build.</summary>
    private FakeAgent AgentOf(Phase phase) => phase is Phase.Review ? Reviewer : Builder;

    public async Task<FactoryRunResult> RunFactoryAsync(CancellationToken cancellationToken = default)
    {
        _kind = RunKind.Factory;
        (Mock<CommandRunner> runner, GitHubClient client, StartBackend backend) = Parts();
        var request = new FactoryRunRequest(
            Owner, Repo, Issue, Token, RunId, _clone.FullName, BaseBranch,
            PlanModel, PlanTurns, BuildModel, BuildTurns, ReviewModel, ReviewTurns, AuthorName, AuthorEmail, SecretNames, SkipIntegration, BaseCommit);
        var run = new FactoryRun(runner.Object, client, backend, Progress, Log, Clock, SummaryPath, Version);
        try
        {
            return await run.RunAsync(request, cancellationToken);
        }
        finally
        {
            Declined = run.Declined;
        }
    }

    public async Task<FeedbackRunResult> RunFeedbackAsync(CancellationToken cancellationToken = default)
    {
        _kind = RunKind.Feedback;
        (Mock<CommandRunner> runner, GitHubClient client, StartBackend backend) = Parts();
        var request = new FeedbackRunRequest(
            Owner, Repo, Pull, Token, _clone.FullName, BuildModel, BuildTurns, ReviewModel, ReviewTurns, AuthorName, AuthorEmail, SecretNames);
        var run = new FeedbackRun(runner.Object, client, backend, Progress, summaryPath: SummaryPath, log: Log, time: Clock);
        try
        {
            return await run.RunAsync(request, cancellationToken);
        }
        finally
        {
            Declined = run.Declined;
        }
    }

    public void Dispose()
    {
        Progress.Dispose();
        Log.Dispose();
        _clone.Delete(recursive: true);
        _root.Delete(recursive: true);
        _files.Delete(recursive: true);
    }

    private (Mock<CommandRunner> Runner, GitHubClient Client, StartBackend Backend) Parts()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        var client = new GitHubClient(new HttpClient(new Handler(this)), Token, TimeSpan.Zero);
        StartBackend backend = FakeBackend.Starter(mock.Object, "factory-sandbox:test", _root.FullName, Builder, Reviewer, Sessions, Clock, Timeline.Add, start => Started = start);
        return (mock, client, backend);
    }

    private void Seen(AgentRequest request)
    {
        AgentRequests.Add(request);
        Timeline.Add($"prompt {request.Phase.Text()}");
        Events.Add(request.Phase.Text());
    }

    private CommandResult Answer(CommandSpec spec)
    {
        Commands.Add(spec);
        if (spec.Command is "git")
        {
            Timeline.Add(string.Join(' ', ["git", .. spec.Arguments]));
            return AnswerGit(spec);
        }

        switch (spec.Arguments[0])
        {
            case "run":
                return new CommandResult { StandardOutput = GateSandbox.ContainerId + "\n" };
            case "exec":
                List<string> arguments = [.. spec.Arguments];
                List<string> words = arguments[(arguments.IndexOf(GateSandbox.ContainerId) + 1)..];
                Timeline.Add(string.Join(' ', ["exec", .. words]));
                return Program(words, arguments.Contains("--workdir"));
            case "rm":
                Events.Add("rm");
                return new CommandResult();
            default:
                return new CommandResult();
        }
    }

    private CommandResult AnswerGit(CommandSpec spec)
    {
        switch (spec.Arguments[0])
        {
            case "clone":
                if (CloneFails)
                {
                    return new CommandResult { ExitCode = 128, StandardError = "fatal: clone failed" };
                }

                Layout(spec.Arguments[2]);
                AfterClone?.Invoke(spec.Arguments[2]);
                return new CommandResult();
            case "checkout" when spec.Arguments is ["checkout", "--detach", string commit]:
                HeadSha = commit;
                return new CommandResult();
            case "checkout":
                return CheckoutFailure is null ? new CommandResult() : new CommandResult { ExitCode = 1, StandardError = CheckoutFailure };
            case "rev-parse":
                return new CommandResult { StandardOutput = HeadSha + "\n" };
            case "status":
                return new CommandResult
                {
                    StandardOutput = Builds is 0
                        ? _kind is RunKind.Factory && Body.Contains("## Scenarios", StringComparison.Ordinal) ? "?? specs/features/\n" : ""
                        : ChangeAfterBuild ? " M src/Checkout.cs\n" : "",
                };
            case "show":
                string path = spec.Arguments[1][(spec.Arguments[1].IndexOf(':', StringComparison.Ordinal) + 1)..];
                return BaseFiles.TryGetValue(path, out string? text)
                    ? new CommandResult { StandardOutput = text }
                    : new CommandResult { ExitCode = 128, StandardError = "fatal: path does not exist" };
            case "diff" when spec.Arguments.Contains("--name-only"):
                List<string> changed = [.. ChangedPaths, .. Builds is 1 ? FirstBuildChanges : []];
                return new CommandResult { StandardOutput = changed.Count is 0 ? "" : string.Join('\n', changed.Distinct()) + "\n" };
            case "diff":
                return new CommandResult { StandardOutput = "diff --git a/src/Checkout.cs b/src/Checkout.cs\n" };
            case "push":
                Events.Add("push");
                return PushFailure is null ? new CommandResult() : new CommandResult { ExitCode = 1, StandardError = PushFailure };
            default:
                return new CommandResult();
        }
    }

    private void Layout(string directory)
    {
        if (!Lacks.Contains("Solution.slnx"))
        {
            File.WriteAllText(Path.Combine(directory, "Solution.slnx"), "<Solution />");
        }

        foreach (string path in new[] { "specs/features/", "tests/Specs/", "tests/Integration/" })
        {
            if (!Lacks.Contains(path))
            {
                Directory.CreateDirectory(Path.Combine(directory, path));
            }
        }

        foreach (string path in CloneFiles)
        {
            string full = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "as on the base commit");
        }
    }

    /// <summary>Answers a program with its words, in the container. <paramref name="inCopy"/> is true for a command that runs in a
    /// directory other than the work tree.</summary>
    private CommandResult Program(List<string> arguments, bool inCopy)
    {
        string program = arguments[0];
        string subcommand = arguments.Count > 1 ? arguments[1] : "";
        int builds = Builds;
        if (program is "sh" && subcommand is "-c")
        {
            (int code, string output, string error) = CheckResults.TryGetValue(builds, out (int Code, string Out, string Err) given)
                ? given
                : _kind is RunKind.Factory && builds is 0 ? (1, "", "check failed") : (0, "", "");
            return new CommandResult { ExitCode = code, StandardOutput = output, StandardError = error };
        }

        if (program is "git" && subcommand is "worktree" && arguments[2] is "add" && WorktreeAddFails)
        {
            return new CommandResult { ExitCode = 128, StandardError = "fatal: cannot add the worktree" };
        }

        if (program is "dotnet" && subcommand is ApiGate.Printer)
        {
            return Printer(builds);
        }

        if (program is "dotnet" && subcommand is "build" && inCopy && CopyBuildResults.TryGetValue(builds, out (int Code, string Out) copy))
        {
            return new CommandResult { ExitCode = copy.Code, StandardOutput = copy.Out };
        }

        if (program is "dotnet" && subcommand is "format" && !arguments.Contains("--verify-no-changes"))
        {
            Clock.Advance(LintSeconds.GetValueOrDefault(builds));
            return LintFails ? new CommandResult { ExitCode = 1, StandardError = "format failed" } : new CommandResult();
        }

        return subcommand is "test" ? Test(arguments, builds) : new CommandResult();
    }

    private CommandResult Printer(int builds)
    {
        if (ApiFails)
        {
            return new CommandResult { ExitCode = 1, StandardError = "printer failed" };
        }

        if (ApiResults.TryGetValue(builds, out (int Code, string Err) fault))
        {
            return new CommandResult { ExitCode = fault.Code, StandardError = fault.Err };
        }

        if (ApiLines is { } lines)
        {
            return new CommandResult { StandardOutput = lines(builds) };
        }

        List<string> listing = builds is 0 ? BaseApi : AfterApi ?? BaseApi;
        return new CommandResult { StandardOutput = string.Concat(listing.Select(line => line + "\n")) };
    }

    private CommandResult Test(List<string> arguments, int builds)
    {
        if (TestCommandThrows)
        {
            throw new CommandStartException("dotnet", new InvalidOperationException("cannot start"));
        }

        Events.Add("dotnet");
        if (builds > 0)
        {
            Clock.Advance(TestSeconds.GetValueOrDefault(builds));
        }

        List<(string Class, string Method, string Outcome, string? Message)> tests =
            [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null)];
        for (int i = 0; i < ExtraPassedTests; i++)
        {
            tests.Add(("Specs.Unit.ExtraTests", $"Passes{i}", "Passed", null));
        }

        bool red = RedBuilds.Contains(builds) || RedTexts.ContainsKey(builds);
        string stdout = TestStdout ?? (red ? RedText : "");
        if (RedTexts.TryGetValue(builds, out string? text))
        {
            const string Lead = "failed test: ";
            stdout = "";
            if (text.StartsWith(Lead, StringComparison.Ordinal))
            {
                string[] parts = text[Lead.Length..].Split(": ", 2);
                int dot = parts[0].LastIndexOf('.');
                tests.Add((parts[0][..dot], parts[0][(dot + 1)..], "Failed", parts[1]));
            }
            else
            {
                stdout = text;
            }
        }
        else if (red)
        {
            tests.Add(("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", RedText));
        }

        foreach ((string testClass, string testMethod) in builds is 0 ? BaseOnlyTests : AfterOnlyTests)
        {
            tests.Add((testClass, testMethod, "Passed", null));
        }

        string directory = Path.Combine(_clone.FullName, arguments[arguments.IndexOf("--results-directory") + 1]["/work/".Length..]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "this-run.trx"), Steps.TrxBuilder.Build(tests));
        return new CommandResult { ExitCode = red ? 1 : 0, StandardOutput = stdout };
    }

    private string PullJson() => PullBody is null
        ? JsonSerializer.Serialize(new { head = new { @ref = HeadRef, sha = HeadSha } })
        : JsonSerializer.Serialize(new { body = PullBody, head = new { @ref = HeadRef, sha = HeadSha } });

    private string IssueJson() => JsonSerializer.Serialize(new
    {
        title = Title,
        body = Body,
        user = new { login = "ada" },
        labels = Labels.Select(name => new { name }).ToArray(),
    });

    private string ThreadsJson()
    {
        IEnumerable<object> nodes = Threads.Select(id => id is ThreadA
            ? Node(id, false, "src/Core/Checkout.cs", NullLine ? null : 14, "sbiondolillo", "The total ignores the second item of the cart.")
            : id is ThreadB
                ? Node(id, false, "tests/Specs/Unit/CheckoutTests.cs", 9, "ada", "Name the unit of the total.")
                : Node(id, true, "README.md", 1, "ada", "Fixed already."));
        return JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { reviewThreads = new { nodes } } } } });
    }

    private static object Node(string id, bool resolved, string path, int? line, string login, string body) => new
    {
        id,
        isResolved = resolved,
        path,
        line,
        comments = new { nodes = new[] { new { author = new { login }, body } } },
    };

    /// <summary>Answers GitHub for both kinds of run: the issue, the list and the opening of the pull requests of a factory run, and
    /// the pull request and the review threads of a feedback run.</summary>
    private (int Status, string Body) Route(string method, string path, string? content)
    {
        if (method is "GET" && path.EndsWith("/pulls", StringComparison.Ordinal))
        {
            return (200, "[]");
        }

        if (method is "GET" && path.Contains("/pulls/", StringComparison.Ordinal))
        {
            return HeadStatus is 200 ? (200, PullJson()) : (HeadStatus, "{\"message\":\"Not Found\"}");
        }

        if (method is "GET")
        {
            return IssueStatus is 200 ? (200, IssueJson()) : (IssueStatus, "{\"message\":\"Not Found\"}");
        }

        if (path.EndsWith("/pulls", StringComparison.Ordinal))
        {
            return (PullStatus, PullStatus is 201
                ? JsonSerializer.Serialize(new { number = PullNumber, html_url = PullUrl })
                : "{\"message\":\"Validation Failed\"}");
        }

        if (path.EndsWith("/graphql", StringComparison.Ordinal))
        {
            return GraphQl(content ?? "");
        }

        if (path.EndsWith("/comments", StringComparison.Ordinal))
        {
            Events.Add("comment");
        }

        return (201, "{}");
    }

    private (int Status, string Body) GraphQl(string content)
    {
        using var json = JsonDocument.Parse(content);
        string query = json.RootElement.GetProperty("query").GetString() ?? "";
        if (query.Contains("reviewThreads", StringComparison.Ordinal))
        {
            return (200, ThreadsJson());
        }

        string threadId = json.RootElement.GetProperty("variables").GetProperty("threadId").GetString() ?? "";
        if (query.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal))
        {
            Events.Add("reply:" + threadId);
            return threadId == ReplyErrorThread
                ? (200, "{\"errors\":[{\"message\":\"Could not resolve\"}],\"data\":null}")
                : (200, "{\"data\":{\"addPullRequestReviewThreadReply\":{\"comment\":{\"id\":\"c\"}}}}");
        }

        Events.Add("resolve:" + threadId);
        return (200, "{\"data\":{\"resolveReviewThread\":{\"thread\":{\"isResolved\":true}}}}");
    }

    private sealed class Handler(RunHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Uri uri = request.RequestUri!;
            harness.Requests.Add(new SeenRequest(request.Method.Method, uri.AbsoluteUri, content));
            (int status, string body) = harness.Route(request.Method.Method, uri.AbsolutePath, content);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
