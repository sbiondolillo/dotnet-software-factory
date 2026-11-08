using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A clock that moves only when the program waits.</summary>
public sealed class FakeTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public TimeSpan Now => TimeSpan.FromTicks(_ticks);

    public override long GetTimestamp() => _ticks;

    public Task WaitAsync(TimeSpan span, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ticks += span.Ticks;
        return Task.CompletedTask;
    }
}

/// <summary>One request GitHub got, with the time on the fake clock.</summary>
public sealed record TimedRequest(string Method, string Path, string? Content, TimeSpan At, string? Authorization = null);

/// <summary>
/// A <see cref="Settle"/> over a mock <see cref="CommandRunner"/> that answers git and the docker commands of the gates, a stub
/// GitHub, and a <see cref="FakeBackend"/> whose two <see cref="FakeAgent"/>s answer the prompts of every run. <see cref="RunAsync"/>
/// starts the console program over the same fakes, for a scenario that starts the program.
/// </summary>
/// <remarks>
/// The agents start with the scripts of an agent that does its work: a plan prompt writes the plan, and the feature file unless
/// the recipe plans alone; a build prompt writes the report that marks the thread A fixed; a review prompt approves. A test changes
/// a script on <see cref="Builder"/> or <see cref="Reviewer"/>. The unit of these tests is the settle over the seam: the options
/// that the program hands the backend are in <see cref="BackendOptions"/>, and what an adapter does has its own tests.
/// </remarks>
public sealed class SettleHarness : IDisposable
{
    public const string Token = "secret-token";

    public const string ThreadA = "PRRT_kwDOUpm1Qc6ofWu5";
    public const string ThreadABody = "The total ignores the second item of the cart.";
    public const string HeadRef = "factory/a1b2c3d4";
    public const string DefaultSha = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";
    public const string RedText = "Assert.Equal() Failure: Values differ";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("console-root-");
    private string _clone = "";
    private int _headReads;
    private int _checkReads;
    private bool _resolved;
    private bool _requested;
    private int _readsAfterRequest;
    private int _threadReads;
    private HttpClient? _http;

    public SettleHarness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Environment["FACTORY_GITHUB_TOKEN"] = Token;
        Environment["ANTHROPIC_API_KEY"] = "secret-key";
        HeadSha = _ => DefaultSha;
        CheckRuns = _ => RunsJson("build", "completed", "success");
        Builder = new FakeAgent(() => _clone) { OnRequest = Seen };
        Reviewer = new FakeAgent(() => _clone) { OnRequest = Seen };
        Builder.Stage(Phase.Plan, "plan.md", PlanPhaseHarness.PlanText);
        Builder.Act(Phase.Plan, _ =>
        {
            if (!PlansAlone)
            {
                Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
            }
        });
        Report("fixed");
        Reviewer.Stage(Phase.Review, "review.json", RunHarness.Approved);
    }

    /// <summary>The agent of the plan and the build prompts of every run.</summary>
    public FakeAgent Builder { get; }

    /// <summary>The agent of the review prompts of every run.</summary>
    public FakeAgent Reviewer { get; }

    /// <summary>Each request of either agent, in order.</summary>
    public List<AgentRequest> AgentRequests { get; } = [];

    /// <summary>The number of build prompts so far, over every run of the program.</summary>
    public int Builds => Builder.Of(Phase.Build).Count();

    public Dictionary<string, string> Environment { get; } = [];

    public string DefaultBranch { get; set; } = "main";

    public int RepoStatus { get; set; } = 200;

    /// <summary>The status code of the answer to a request whose path ends with this text, or null.</summary>
    public (string Suffix, int Status)? Refusal { get; set; }

    public int InspectExit { get; set; }

    /// <summary>The labels of the issue.</summary>
    public List<string> Labels { get; } = [];

    /// <summary>The body of the issue, or null for the default.</summary>
    public string? IssueBody { get; set; }

    /// <summary>The body of the pull request, or null for an answer with no body.</summary>
    public string? PullBody { get; set; }

    /// <summary>The labels of the issue 2, which a <see cref="PullBody"/> of "Closes #2" names, or null for <see cref="Labels"/>.</summary>
    public List<string>? ClosingLabels { get; set; }

    /// <summary>What the check command answers after the build of that number (0 is before the first build); a number with no entry answers red before the first build and green after one.</summary>
    public Dictionary<int, (int Code, string Out, string Err)> CheckResults { get; } = [];

    public HashSet<string> Lacks { get; } = [];

    public Func<int, string> HeadSha { get; set; }

    public Func<int, string> CheckRuns { get; set; }

    public string Reviews { get; set; } = ReviewsJson("copilot-pull-request-reviewer");

    /// <summary>The answer to a read of the reviews from the number of reads after the review request (0 before it), or null for <see cref="Reviews"/>.</summary>
    public Func<int, string>? ReviewsByRead { get; set; }

    /// <summary>"none", "always" (thread A unresolved on each read, as when each push brings a new review with a new thread), "until-resolved" (thread A until its resolve, then no thread), "resolvable" (thread A on each read,
    /// with <c>isResolved</c> true after its resolve, as GitHub answers) or "watch-only" (thread A on each odd read, which is a watch's,
    /// and no thread on each even read, which is the start of a feedback run, so each feedback run finds no thread and pushes nothing).</summary>
    public string Threads { get; set; } = "none";

    /// <summary>The message of a GraphQL error that answers the review request, or null for success.</summary>
    public string? ReviewRequestError { get; set; }

    /// <summary>The message of the failed test of a red build.</summary>
    public string FailureText { get; set; } = RedText;

    public HashSet<int> RedBuilds { get; } = [];

    public int PullNumber { get; set; } = 10;

    public string PullUrl { get; set; } = "https://github.com/sbiondolillo/factory-scratch/pull/10";

    /// <summary>What standard output held when each prompt of <see cref="AgentRequests"/> ran, by index.</summary>
    public List<string> OutAtPrompt { get; } = [];

    public FakeTime Time { get; } = new();

    /// <summary>The exit code of <c>git worktree add</c> in the container.</summary>
    public int WorktreeAddExit { get; set; }

    /// <summary>The lines the API printer prints after the build of that number (0 is before the first build); a number with no entry prints nothing.</summary>
    public Dictionary<int, string> ApiOutputs { get; } = [];

    /// <summary>The exit code of a dotnet command in the container, by its program and words joined by one space.</summary>
    public Dictionary<string, int> DotnetExits { get; } = [];

    public string Root => _root.FullName;

    /// <summary>The options that the program handed the backend of each run, in order.</summary>
    public List<BackendOptions> BackendOptions { get; } = [];

    /// <summary>Puts the variables of the Managed Agents sessions in the environment, as the option <c>--agent managed</c> needs.</summary>
    public void AddManagedVariables()
    {
        foreach ((string name, string value) in ManagedRunFake.Environment())
        {
            Environment[name] = value;
        }
    }

    /// <summary>Scripts the report of each build prompt, which gives the thread A the status <paramref name="status"/>.</summary>
    public void Report(string status) => Builder.Stage(Phase.Build, "threads.json", JsonSerializer.Serialize(new
    {
        threads = new[] { new { id = ThreadA, status, reply = "The total now adds every item of the cart." } },
    }));

    /// <summary>Scripts each plan prompt to write the plan file and no feature file.</summary>
    public void PlanWritesOnlyThePlan()
    {
        Builder.Clear(Phase.Plan);
        Builder.Stage(Phase.Plan, "plan.md", PlanPhaseHarness.PlanText);
    }

    /// <summary>Scripts the review prompt <paramref name="number"/> to block the build on the thread A.</summary>
    public void BlockReview(int number) => Reviewer.Stage(
        Phase.Review, "review.json", JsonSerializer.Serialize(new { approved = false, blocking = new[] { ThreadABody } }), number);

    public List<CommandSpec> Commands { get; } = [];

    public List<TimedRequest> Requests { get; } = [];

    public List<TimeSpan> PushTimes { get; } = [];

    public List<string> CloneDirectories { get; } = [];

    public StringWriter Out { get; } = new();

    public StringWriter Error { get; } = new();

    public IEnumerable<TimedRequest> Posts(string suffix) =>
        Requests.Where(r => r.Method is "POST" && r.Path.EndsWith(suffix, StringComparison.Ordinal));

    public static IEnumerable<string> Lines(StringWriter writer) =>
        writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public static string RunsJson(string name, string status, string? conclusion) =>
        JsonSerializer.Serialize(new { check_runs = new[] { new { name, status, conclusion } } });

    public static string ReviewsJson(string login, string commit = DefaultSha, string body = "") =>
        JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { reviews = new { nodes = new[] { new { state = "COMMENTED", body, author = new { login }, commit = new { oid = commit } } } } } } } });

    public static string ReviewsJson(params (string Login, string Commit, string Body)[] reviews) =>
        JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { reviews = new { nodes = reviews.Select(r => new { state = "COMMENTED", body = r.Body, author = new { login = r.Login }, commit = new { oid = r.Commit } }).ToArray() } } } } });

    public static string NoReviewsJson() =>
        JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { reviews = new { nodes = Array.Empty<object>() } } } } });

    /// <summary>The options of a settle: <paramref name="rounds"/> feedback rounds, no Copilot review unless asked, and a watch of
    /// <paramref name="timeoutMinutes"/> minutes.</summary>
    public static SettleOptions Options(int rounds = 3, bool copilotReview = false, int timeoutMinutes = 60) =>
        new(rounds, copilotReview, TimeSpan.FromMinutes(timeoutMinutes), [], false);

    /// <summary>Settles the issue <paramref name="issue"/> of sbiondolillo/factory-scratch.</summary>
    public Task<SettleResult> SettleIssueAsync(SettleOptions options, int issue = 1, CancellationToken cancellationToken = default) =>
        Settle().SettleIssueAsync(new IssueNumber(Repo, issue), null, options, cancellationToken);

    /// <summary>Settles the pull request <see cref="PullNumber"/> of sbiondolillo/factory-scratch.</summary>
    public Task<SettleResult> SettlePullRequestAsync(SettleOptions options, CancellationToken cancellationToken = default) =>
        Settle().SettlePullRequestAsync(new IssueNumber(Repo, PullNumber), options, cancellationToken);

    /// <summary>Starts the console program with <paramref name="args"/> over the fakes, and gives its exit code.</summary>
    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var host = new ConsoleHost(
            name => Environment.GetValueOrDefault(name),
            Runner(),
            new Handler(this),
            Out,
            Error,
            Root,
            Time.WaitAsync,
            Time,
            Backend,
            SettleSettings.Default);
        return ConsoleProgram.RunAsync(args, host, cancellationToken);
    }

    public void Dispose()
    {
        _http?.Dispose();
        _root.Delete(recursive: true);
    }

    private static RepoName Repo => new("sbiondolillo", "factory-scratch");

    private Settle Settle()
    {
        CommandRunner runner = Runner();
        _http ??= new HttpClient(new Handler(this));
        var client = new GitHubClient(_http, Token);
        StartBackend backend = FakeBackend.Starter(runner, "dotnet-software-factory-sandbox", Root, Builder, Reviewer, time: Time);
        return new Settle(runner, client, client, Token, backend, SettleSettings.Default, Out, Error, Time, Time.WaitAsync, Environment.GetValueOrDefault("GITHUB_STEP_SUMMARY"));
    }

    private CommandRunner Runner()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        return mock.Object;
    }

    /// <summary>True for a recipe whose plan prompt asks for the plan alone: a refactor, or an issue with scenarios or with
    /// removed scenarios.</summary>
    private bool PlansAlone =>
        Labels.Contains("refactor")
        || (IssueBody ?? "").Contains("## Scenarios", StringComparison.Ordinal)
        || (IssueBody ?? "").Contains("## Removed scenarios", StringComparison.Ordinal);

    /// <summary>The fake backend of each run of the program, over the options that the program hands it.</summary>
    private StartBackend Backend(BackendOptions options)
    {
        BackendOptions.Add(options);
        return FakeBackend.Starter(options.Runner, options.ImageTag, options.RootDirectory, Builder, Reviewer, time: options.Time);
    }

    private void Seen(AgentRequest request)
    {
        AgentRequests.Add(request);
        OutAtPrompt.Add(Out.ToString());
    }

    private CommandResult Answer(CommandSpec spec)
    {
        Commands.Add(spec);
        if (spec.Command is "git")
        {
            return AnswerGit(spec);
        }

        return spec.Arguments[0] switch
        {
            "image" => new CommandResult { ExitCode = InspectExit },
            "run" => new CommandResult { StandardOutput = GateSandbox.ContainerId + "\n" },
            "exec" => Exec(spec),
            _ => new CommandResult(),
        };
    }

    private CommandResult AnswerGit(CommandSpec spec)
    {
        switch (spec.Arguments[0])
        {
            case "clone":
                _clone = spec.Arguments[2];
                CloneDirectories.Add(_clone);
                Layout(_clone);
                return new CommandResult();
            case "rev-parse":
                return new CommandResult { StandardOutput = DefaultSha + "\n" };
            case "status":
                return new CommandResult { StandardOutput = Builds is 0 ? "" : " M src/Checkout.cs\n" };
            case "diff" when spec.Arguments.Contains("--name-only"):
                return new CommandResult();
            case "diff":
                return new CommandResult { StandardOutput = "diff --git a/src/Checkout.cs b/src/Checkout.cs\n" };
            case "push":
                PushTimes.Add(Time.Now);
                return new CommandResult();
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
    }

    private CommandResult Exec(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        int id = arguments.IndexOf(GateSandbox.ContainerId);
        return Program(arguments[(id + 1)..], directory => Path.Combine(_clone, directory["/work/".Length..]));
    }

    /// <summary>Answers a program with its words, in the container. <paramref name="results"/> maps the TRX directory to the host.</summary>
    private CommandResult Program(List<string> command, Func<string, string> results)
    {
        if (command[0] is "sh" && command[1] is "-c")
        {
            (int code, string output, string error) = CheckResults.TryGetValue(Builds, out (int Code, string Out, string Err) given) ? given : Builds is 0 ? (1, "", "check failed") : (0, "", "");
            return new CommandResult { ExitCode = code, StandardOutput = output, StandardError = error };
        }

        if (command[0] is "git" && command[1] is "worktree" && command[2] is "add")
        {
            return new CommandResult { ExitCode = WorktreeAddExit, StandardError = "no worktree" };
        }

        if (command[0] is "dotnet" && command[1] is "/opt/api-printer/ApiPrinter.dll")
        {
            return new CommandResult { StandardOutput = ApiOutputs.GetValueOrDefault(Builds, "") };
        }

        if (command[0] is "dotnet" && DotnetExits.TryGetValue(string.Join(' ', command), out int exit))
        {
            return new CommandResult { ExitCode = exit };
        }

        if (command.Count < 2 || command[1] is not "test")
        {
            return new CommandResult();
        }

        bool red = RedBuilds.Contains(Builds);
        List<(string Class, string Method, string Outcome, string? Message)> tests =
            [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null)];
        if (red)
        {
            tests.Add(("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", FailureText));
        }

        string directory = results(command[command.IndexOf("--results-directory") + 1]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "this-run.trx"), Steps.TrxBuilder.Build(tests));
        return new CommandResult { ExitCode = red ? 1 : 0, StandardOutput = red ? FailureText : "" };
    }

    /// <summary>Writes a file of the agent into the clone.</summary>
    private void Write(string relative, string text)
    {
        string full = Path.Combine(_clone, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private (int Status, string Body) Route(string method, string path, string? content)
    {
        if (Refusal is { } refusal && path.EndsWith(refusal.Suffix, StringComparison.Ordinal))
        {
            return (refusal.Status, "{\"message\":\"refused\"}");
        }

        if (path.EndsWith("/graphql", StringComparison.Ordinal))
        {
            return GraphQl(content ?? "");
        }

        if (path.EndsWith("/check-runs", StringComparison.Ordinal))
        {
            return (200, CheckRuns(++_checkReads));
        }

        if (path.EndsWith("/comments", StringComparison.Ordinal))
        {
            return (201, "{}");
        }

        if (path.EndsWith("/pulls", StringComparison.Ordinal))
        {
            return method is "GET"
                ? (200, "[]")
                : (201, JsonSerializer.Serialize(new { number = PullNumber, html_url = PullUrl }));
        }

        if (path.Contains("/pulls/", StringComparison.Ordinal))
        {
            return (200, PullBody is null
                ? JsonSerializer.Serialize(new { node_id = "PR_kwDOUpm1Qc6abc", head = new { sha = HeadSha(++_headReads), @ref = HeadRef } })
                : JsonSerializer.Serialize(new { node_id = "PR_kwDOUpm1Qc6abc", body = PullBody, head = new { sha = HeadSha(++_headReads), @ref = HeadRef } }));
        }

        if (path.Contains("/issues/", StringComparison.Ordinal))
        {
            List<string> labels = ClosingLabels is not null && path.EndsWith("/issues/2", StringComparison.Ordinal) ? ClosingLabels : Labels;
            return (200, JsonSerializer.Serialize(new { title = "Add a checkout total", body = IssueBody ?? "The checkout shows the total of the cart.", user = new { login = "ada" }, labels = labels.Select(name => new { name }).ToArray() }));
        }

        return RepoStatus is 200
            ? (200, JsonSerializer.Serialize(new { default_branch = DefaultBranch }))
            : (RepoStatus, "{\"message\":\"Not Found\"}");
    }

    private (int Status, string Body) GraphQl(string content)
    {
        using var json = JsonDocument.Parse(content);
        string query = json.RootElement.GetProperty("query").GetString() ?? "";
        if (query.Contains("reviewThreads", StringComparison.Ordinal))
        {
            bool listed = Threads is "always" or "resolvable" || (Threads is "until-resolved" && !_resolved) || (Threads is "watch-only" && ++_threadReads % 2 is 1);
            bool isResolved = Threads is "resolvable" && _resolved;
            var nodes = listed
                ? new[] { new { id = ThreadA, isResolved, path = "src/Core/Checkout.cs", line = (int?)14, comments = new { nodes = new[] { new { author = new { login = "sbiondolillo" }, body = ThreadABody } } } } }
                : [];
            return (200, JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { reviewThreads = new { nodes } } } } }));
        }

        if (query.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal))
        {
            return (200, "{\"data\":{\"addPullRequestReviewThreadReply\":{\"comment\":{\"id\":\"c\"}}}}");
        }

        if (query.Contains("resolveReviewThread", StringComparison.Ordinal))
        {
            _resolved = true;
            return (200, "{\"data\":{\"resolveReviewThread\":{\"thread\":{\"isResolved\":true}}}}");
        }

        if (query.Contains("requestReviewsByLogin", StringComparison.Ordinal))
        {
            _requested = true;
            return ReviewRequestError is { } message
                ? (200, JsonSerializer.Serialize(new { data = (object?)null, errors = new[] { new { message } } }))
                : (200, "{\"data\":{\"requestReviewsByLogin\":{\"pullRequest\":{\"id\":\"PR_kwDOUpm1Qc6abc\"}}}}");
        }

        if (ReviewsByRead is { } byRead)
        {
            return (200, byRead(_requested ? ++_readsAfterRequest : 0));
        }

        return (200, Reviews);
    }

    private sealed class Handler(SettleHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string path = request.RequestUri!.AbsolutePath;
            harness.Requests.Add(new TimedRequest(request.Method.Method, path, content, harness.Time.Now, request.Headers.Authorization?.ToString()));
            (int status, string body) = harness.Route(request.Method.Method, path, content);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
