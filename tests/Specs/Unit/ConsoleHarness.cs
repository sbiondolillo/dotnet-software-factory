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

/// <summary>The console program over a mock <see cref="CommandRunner"/> that answers git, docker and claude, and a stub GitHub.</summary>
public sealed class ConsoleHarness : IDisposable
{
    public const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string ThreadA = "PRRT_kwDOUpm1Qc6ofWu5";
    public const string ThreadABody = "The total ignores the second item of the cart.";
    public const string HeadRef = "factory/a1b2c3d4";
    public const string DefaultSha = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";
    public const string RedText = "Assert.Equal() Failure: Values differ";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("console-root-");
    private string _clone = "";
    private int _builds;
    private int _reviews;
    private int _headReads;
    private int _checkReads;
    private bool _resolved;
    private bool _requested;
    private int _readsAfterRequest;

    public ConsoleHarness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Environment["FACTORY_GITHUB_TOKEN"] = "secret-token";
        Environment["ANTHROPIC_API_KEY"] = "secret-key";
        HeadSha = _ => DefaultSha;
        CheckRuns = _ => RunsJson("build", "completed", "success");
    }

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

    /// <summary>What the check command answers after the build of that number (0 is before the first build); a number with no entry answers red before the first build and green after one.</summary>
    public Dictionary<int, (int Code, string Out, string Err)> CheckResults { get; } = [];

    public HashSet<string> Lacks { get; } = [];

    public Func<int, string> HeadSha { get; set; }

    public Func<int, string> CheckRuns { get; set; }

    public string Reviews { get; set; } = ReviewsJson("copilot-pull-request-reviewer");

    /// <summary>The answer to a read of the reviews from the number of reads after the review request (0 before it), or null for <see cref="Reviews"/>.</summary>
    public Func<int, string>? ReviewsByRead { get; set; }

    /// <summary>"none", "always" or "until-resolved".</summary>
    public string Threads { get; set; } = "none";

    public bool Disputed { get; set; }

    /// <summary>The message of a GraphQL error that answers the review request, or null for success.</summary>
    public string? ReviewRequestError { get; set; }

    /// <summary>The message of the failed test of a red build.</summary>
    public string FailureText { get; set; } = RedText;

    public HashSet<int> RedBuilds { get; } = [];

    public int PullNumber { get; set; } = 10;

    public string PullUrl { get; set; } = "https://github.com/sbiondolillo/factory-scratch/pull/10";

    public bool AgentWritesPlan { get; set; } = true;

    /// <summary>True when the agent writes the plan file and no feature file on the plan prompt.</summary>
    public bool AgentWritesOnlyPlan { get; set; }

    /// <summary>The number of review prompts that get a blocked verdict, from the first.</summary>
    public int BlockedReviews { get; set; }

    /// <summary>What standard output held when each program of <see cref="Commands"/> ran, by index.</summary>
    public List<string> OutAtCommand { get; } = [];

    /// <summary>What standard output held when each prompt of <see cref="Prompts"/> ran, by index.</summary>
    public List<string> OutAtPrompt { get; } = [];

    public FakeTime Time { get; } = new();

    /// <summary>The exit code of <c>git worktree add</c> in the container.</summary>
    public int WorktreeAddExit { get; set; }

    /// <summary>The answer of claude to a prompt kind: turns, the cost as written, and the seconds the answer takes.</summary>
    public Dictionary<string, (int Turns, string Cost, TimeSpan Elapsed)> Answers { get; } = [];

    /// <summary>The lines the API printer prints after the build of that number (0 is before the first build); a number with no entry prints nothing.</summary>
    public Dictionary<int, string> ApiOutputs { get; } = [];

    /// <summary>The exit code of a dotnet command in the container, by its program and words joined by one space.</summary>
    public Dictionary<string, int> DotnetExits { get; } = [];

    /// <summary>What standard error held when each prompt of <see cref="Prompts"/> ran, by index.</summary>
    public List<string> ErrorAtPrompt { get; } = [];

    public string Root => _root.FullName;

    /// <summary>The fake of the Managed Agents API, or null on the path of Claude Code in a sandbox.</summary>
    public ManagedRunFake? Managed { get; private set; }

    /// <summary>The requests that the fake of the Managed Agents API and of the App tokens got, apart from <see cref="Requests"/>.</summary>
    public List<TimedRequest> ManagedRequests { get; } = [];

    /// <summary>Each git command, as its words, and each request to the fake of the Managed Agents API, as its method and path, in order.</summary>
    public List<string> Timeline { get; } = [];

    /// <summary>The cost in cents of one answer of a session, by prompt kind.</summary>
    public Dictionary<string, int> SessionCents { get; } = [];

    /// <summary>The builds, by number, after which the session pushes no commit.</summary>
    public HashSet<int> NoPushBuilds { get; } = [];

    /// <summary>Puts the run on the managed path: the variables of the sessions in the environment, and the fake API.</summary>
    public void UseManaged()
    {
        foreach ((string name, string value) in ManagedRunFake.Environment())
        {
            Environment[name] = value;
        }

        Managed = new ManagedRunFake { OnMessage = SessionMessage };
    }

    public List<CommandSpec> Commands { get; } = [];

    public List<TimedRequest> Requests { get; } = [];

    public List<TimeSpan> PushTimes { get; } = [];

    public List<string> CloneDirectories { get; } = [];

    public List<(string Kind, string Model, string Turns, string Text)> Prompts { get; } = [];

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

    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        var host = new ConsoleHost(
            name => Environment.GetValueOrDefault(name),
            mock.Object,
            new Handler(this),
            Out,
            Error,
            Root,
            Time.WaitAsync,
            Time);
        return ConsoleProgram.RunAsync(args, host, cancellationToken);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private static string Json(string text) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["is_error"] = false, ["result"] = text, ["num_turns"] = 1, ["total_cost_usd"] = 0.0 });

    private CommandResult Answer(CommandSpec spec)
    {
        Commands.Add(spec);
        OutAtCommand.Add(Out.ToString());
        if (spec.Command is "git")
        {
            Timeline.Add(string.Join(' ', ["git", .. spec.Arguments]));
            return AnswerGit(spec);
        }

        if (spec.Command is not "docker")
        {
            return Program([spec.Command, .. spec.Arguments], directory => directory);
        }

        return spec.Arguments[0] switch
        {
            "image" => new CommandResult { ExitCode = InspectExit },
            "run" => new CommandResult { StandardOutput = ContainerId + "\n" },
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
                return new CommandResult { StandardOutput = _builds is 0 ? "" : " M src/Checkout.cs\n" };
            case "diff" when spec.Arguments.Contains("--name-only"):
                return new CommandResult();
            case "diff":
                return new CommandResult { StandardOutput = "diff --git a/src/Checkout.cs b/src/Checkout.cs\n" };
            case "push":
                PushTimes.Add(Time.Now);
                return new CommandResult();
            case "pull":
                Managed?.Pull(_clone);
                return new CommandResult();
            case "ls-remote" when Managed is not null:
                return new CommandResult { StandardOutput = Managed.LsRemote(spec.Arguments) };
            case "ls-files" when Managed is not null:
                return new CommandResult { StandardOutput = string.Concat(Managed.TrackedFactoryFiles.Select(path => path + "\n")) };
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
        int id = arguments.IndexOf(ContainerId);
        if (arguments[id + 1] is "claude")
        {
            return Claude(arguments);
        }

        return Program(arguments[(id + 1)..], directory => Path.Combine(_clone, directory["/work/".Length..]));
    }

    /// <summary>Answers a program with its words, in the container or on the host. <paramref name="results"/> maps the TRX directory to the host.</summary>
    private CommandResult Program(List<string> command, Func<string, string> results)
    {
        if (command[0] is "sh" && command[1] is "-c")
        {
            (int code, string output, string error) = CheckResults.TryGetValue(_builds, out (int Code, string Out, string Err) given) ? given : _builds is 0 ? (1, "", "check failed") : (0, "", "");
            return new CommandResult { ExitCode = code, StandardOutput = output, StandardError = error };
        }

        if (command[0] is "git" && command[1] is "worktree" && command[2] is "add")
        {
            return new CommandResult { ExitCode = WorktreeAddExit, StandardError = "no worktree" };
        }

        if (command[0] is "dotnet" && command[1] is "/opt/api-printer/ApiPrinter.dll")
        {
            return new CommandResult { StandardOutput = ApiOutputs.GetValueOrDefault(_builds, "") };
        }

        if (command[0] is "dotnet" && DotnetExits.TryGetValue(string.Join(' ', command), out int exit))
        {
            return new CommandResult { ExitCode = exit };
        }

        if (command.Count < 2 || command[1] is not "test")
        {
            return new CommandResult();
        }

        bool red = RedBuilds.Contains(_builds);
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

    private CommandResult Claude(List<string> arguments)
    {
        string text = arguments[arguments.IndexOf("-p") + 1];
        string model = arguments[arguments.IndexOf("--model") + 1];
        string turns = arguments[arguments.IndexOf("--max-turns") + 1];
        string kind = text.StartsWith("Plan the work", StringComparison.Ordinal) ? "plan"
            : text.StartsWith("Review the build", StringComparison.Ordinal) ? "review" : "build";
        Turn(kind, model, turns, text);
        if (!Answers.TryGetValue(kind, out (int Turns, string Cost, TimeSpan Elapsed) answer))
        {
            return new CommandResult { StandardOutput = Json("done") };
        }

        Time.WaitAsync(answer.Elapsed, CancellationToken.None).GetAwaiter().GetResult();
        return new CommandResult
        {
            StandardOutput = $"{{\"is_error\":false,\"result\":\"done\",\"num_turns\":{answer.Turns},\"total_cost_usd\":{answer.Cost}}}",
        };
    }

    /// <summary>Plays the agent of a session for one message. A preamble may come before the prompt, so the kind is found anywhere in the text.</summary>
    private ManagedTurn SessionMessage(string text, string model)
    {
        string kind = text.Contains("Plan the work", StringComparison.Ordinal) ? "plan"
            : text.Contains("Review the build", StringComparison.Ordinal) ? "review" : "build";
        Turn(kind, model, "", text);
        return new ManagedTurn("done", Cents: SessionCents.GetValueOrDefault(kind), Pushes: kind is "build" && !NoPushBuilds.Contains(_builds));
    }

    /// <summary>Records one prompt and writes what the agent writes for it.</summary>
    private void Turn(string kind, string model, string turns, string text)
    {
        if (kind is "build")
        {
            _builds++;
        }

        Prompts.Add((kind, model, turns, text));
        OutAtPrompt.Add(Out.ToString());
        ErrorAtPrompt.Add(Error.ToString());
        if (kind is "plan" && AgentWritesPlan)
        {
            if (!AgentWritesOnlyPlan && !Labels.Contains("refactor"))
            {
                Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
            }

            Write(".factory/plan.md", PlanPhaseHarness.PlanText);
        }

        if (kind is "build" && text.Contains(".factory/threads.json", StringComparison.Ordinal))
        {
            Write(".factory/threads.json", JsonSerializer.Serialize(new
            {
                threads = new[] { new { id = ThreadA, status = Disputed ? "disputed" : "fixed", reply = "The total now adds every item of the cart." } },
            }));
        }

        if (kind is "review")
        {
            _reviews++;
            Write(
                ".factory/review.json",
                _reviews <= BlockedReviews
                    ? JsonSerializer.Serialize(new { approved = false, blocking = new[] { ThreadABody } })
                    : """{"approved":true,"blocking":[]}""");
        }
    }

    /// <summary>Writes a file of the agent. On the managed path a tracked file waits for the pull, and a file under .factory/ is also an output of the session.</summary>
    private void Write(string relative, string text)
    {
        if (Managed is not null && !relative.StartsWith(".factory/", StringComparison.Ordinal))
        {
            Managed.Stage(relative, text);
            return;
        }

        Managed?.AddOutput(relative, text);
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
            return (200, JsonSerializer.Serialize(new { node_id = "PR_kwDOUpm1Qc6abc", head = new { sha = HeadSha(++_headReads), @ref = HeadRef } }));
        }

        if (path.Contains("/issues/", StringComparison.Ordinal))
        {
            return (200, JsonSerializer.Serialize(new { title = "Add a checkout total", body = IssueBody ?? "The checkout shows the total of the cart.", user = new { login = "ada" }, labels = Labels.Select(name => new { name }).ToArray() }));
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
            bool listed = Threads is "always" || (Threads is "until-resolved" && !_resolved);
            var nodes = listed
                ? new[] { new { id = ThreadA, isResolved = false, path = "src/Core/Checkout.cs", line = (int?)14, comments = new { nodes = new[] { new { author = new { login = "sbiondolillo" }, body = ThreadABody } } } } }
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

    private sealed class Handler(ConsoleHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string path = request.RequestUri!.AbsolutePath;
            if (harness.Managed is { } managed && ManagedRunFake.Handles(request.RequestUri))
            {
                harness.ManagedRequests.Add(new TimedRequest(request.Method.Method, request.RequestUri.PathAndQuery, content, harness.Time.Now));
                harness.Timeline.Add($"{request.Method.Method} {path}");
                (int code, string answer) = managed.Route(request.Method.Method, request.RequestUri, content);
                return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
            }

            harness.Requests.Add(new TimedRequest(request.Method.Method, path, content, harness.Time.Now, request.Headers.Authorization?.ToString()));
            (int status, string body) = harness.Route(request.Method.Method, path, content);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
