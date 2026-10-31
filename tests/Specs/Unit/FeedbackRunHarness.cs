using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A real clone directory, a mock <see cref="CommandRunner"/> that answers git and docker, and a stub GitHub, for a <see cref="FeedbackRun"/>.</summary>
public sealed class FeedbackRunHarness : IDisposable
{
    public const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string ThreadA = "PRRT_kwDOUpm1Qc6ofWu5";
    public const string ThreadB = "PRRT_kwDOUpm1Qc6ofWvu";
    public const string ThreadC = "PRRT_kwDOUpm1Qc6ofWxx";
    public const string ReplyA = "The total now adds every item of the cart.";
    public const string ReplyB = "The title of the test names the unit.";
    public const string Blocking = "The total of an empty cart is unset.";
    public const string RedText = "Assert.Equal() Failure: Values differ";

    private readonly DirectoryInfo _clone = Directory.CreateTempSubdirectory("feedback-run-clone-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("feedback-run-root-");
    private int _builds;

    public FeedbackRunHarness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Threads = [ThreadA, ThreadB];
    }

    public string Owner { get; set; } = "sbiondolillo";

    public string Repo { get; set; } = "factory-scratch";

    public int Pull { get; set; } = 10;

    public string Token { get; set; } = "token";

    public List<string> SecretNames { get; set; } = [];

    public string BuildModel { get; set; } = "build-model";

    public int BuildTurns { get; set; } = 60;

    public string ReviewModel { get; set; } = "review-model";

    public int ReviewTurns { get; set; } = 30;

    public string AuthorName { get; set; } = "Factory";

    public string AuthorEmail { get; set; } = "factory@example.com";

    public string HeadRef { get; set; } = "factory/a1b2c3d4";

    public string HeadSha { get; set; } = "c7d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293";

    /// <summary>The ids of the threads GitHub lists, in order: A, B and C (resolved) are known.</summary>
    public List<string> Threads { get; set; }

    public bool NullLine { get; set; }

    public bool ChangeAfterBuild { get; set; } = true;

    public bool BuildIsError { get; set; }

    public HashSet<int> ErrorBuilds { get; } = [];

    public HashSet<int> RedBuilds { get; } = [];

    public HashSet<int> RejectedBuilds { get; } = [];

    public string? TestStdout { get; set; }

    public int HeadStatus { get; set; } = 200;

    /// <summary>The body of the pull request, or null for an answer with no body.</summary>
    public string? PullBody { get; set; }

    public int IssueStatus { get; set; } = 200;

    public List<string> Labels { get; } = [];

    public string IssueBody { get; set; } = "";

    public List<string> FirstBuildChanges { get; } = [];

    /// <summary>The files the first build writes into the clone.</summary>
    public List<string> FirstBuildWrites { get; } = [];

    /// <summary>The files the first build adds: they are absent on the head commit and present after the build.</summary>
    public List<string> FirstBuildAdds { get; } = [];

    public Dictionary<int, (int Code, string Out, string Err)> CheckResults { get; } = [];

    public List<(string Class, string Method)> BaseOnlyTests { get; } = [];

    public StringWriter Out { get; } = new();

    public string? ReplyErrorThread { get; set; }

    public string? CheckoutFailure { get; set; }

    public string? PushFailure { get; set; }

    public bool CloneFails { get; set; }

    /// <summary>Gives the report text a build writes, or null for no file. The default marks every thread fixed.</summary>
    public Func<int, string?> Report { get; set; } = _ => FixedReport(ThreadA, ThreadB);

    public string CloneDirectory => _clone.FullName;

    /// <summary>The fake of the Managed Agents API, or null on the path of Claude Code in a sandbox.</summary>
    public ManagedRunFake? Managed { get; private set; }

    /// <summary>The builds, by number, after which the session pushes no commit.</summary>
    public HashSet<int> NoPushBuilds { get; } = [];

    /// <summary>Each git command, as its words, and each request to the fake of the Managed Agents API, as its method and path, in order.</summary>
    public List<string> Timeline { get; } = [];

    /// <summary>Puts the run on the managed path.</summary>
    public void UseManaged() => Managed = new ManagedRunFake { OnMessage = SessionMessage };

    public List<CommandSpec> Commands { get; } = [];

    public List<SeenPrompt> Prompts { get; } = [];

    public List<SeenRequest> Requests { get; } = [];

    public List<string> Events { get; } = [];

    public int Builds => _builds;

    public IEnumerable<SeenPrompt> BuildPrompts => Prompts.Where(p => p.Kind is "build");

    public IEnumerable<string> CommentBodies =>
        Requests.Where(r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal))
            .Select(c => JsonDocument.Parse(c.Content!).RootElement.GetProperty("body").GetString() ?? "");

    public static string FixedReport(params string[] ids) => Items(ids.Select(id => (id, "fixed", id == ThreadA ? ReplyA : ReplyB)));

    public static string Items(IEnumerable<(string Id, string Status, string Reply)> items) =>
        JsonSerializer.Serialize(new { threads = items.Select(i => new { id = i.Id, status = i.Status, reply = i.Reply }) });

    public Task<FeedbackRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        var client = new GitHubClient(new HttpClient(new Handler(this)), Token, TimeSpan.Zero);
        var request = new FeedbackRunRequest(
            Owner, Repo, Pull, Token, _clone.FullName, "factory-sandbox:test", _root.FullName,
            BuildModel, BuildTurns, ReviewModel, ReviewTurns, AuthorName, AuthorEmail, SecretNames, Managed: Managed is null ? null : ManagedRunFake.Settings());
        return new FeedbackRun(mock.Object, client, Out, handler: new Handler(this), delay: (_, _) => Task.CompletedTask).RunAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        _clone.Delete(recursive: true);
        _root.Delete(recursive: true);
    }

    private static string Json(bool isError, string text) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["is_error"] = isError,
            ["result"] = text,
            ["num_turns"] = 1,
            ["total_cost_usd"] = 0.0,
        });

    private CommandResult Answer(CommandSpec spec)
    {
        Commands.Add(spec);
        if (spec.Command is "git")
        {
            Timeline.Add(string.Join(' ', ["git", .. spec.Arguments]));
            return AnswerGit(spec);
        }

        if (spec.Command is not "docker")
        {
            return Program([spec.Command, .. spec.Arguments], directory => directory);
        }

        switch (spec.Arguments[0])
        {
            case "run":
                return new CommandResult { StandardOutput = ContainerId + "\n" };
            case "exec":
                return Exec(spec);
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

                Directory.CreateDirectory(Path.Combine(spec.Arguments[2], "specs", "features"));
                if (Labels.Contains("refactor"))
                {
                    foreach (string path in FirstBuildChanges.Except(FirstBuildAdds))
                    {
                        Write(path, "as on the head commit");
                    }
                }

                return new CommandResult();
            case "checkout":
                return CheckoutFailure is null ? new CommandResult() : new CommandResult { ExitCode = 1, StandardError = CheckoutFailure };
            case "rev-parse":
                return new CommandResult { StandardOutput = HeadSha + "\n" };
            case "status":
                return new CommandResult { StandardOutput = ChangeAfterBuild ? " M src/Checkout.cs\n" : "" };
            case "diff" when spec.Arguments.Contains("--name-only"):
                return new CommandResult { StandardOutput = _builds is 1 ? string.Join('\n', FirstBuildChanges) + "\n" : "" };
            case "diff":
                return new CommandResult { StandardOutput = "diff --git a/src/Checkout.cs b/src/Checkout.cs\n" };
            case "push":
                Events.Add("push");
                return PushFailure is null ? new CommandResult() : new CommandResult { ExitCode = 1, StandardError = PushFailure };
            case "pull":
                Managed?.Pull(_clone.FullName);
                return new CommandResult();
            case "ls-remote" when Managed is not null:
                return new CommandResult { StandardOutput = Managed.LsRemote(spec.Arguments) };
            case "ls-files" when Managed is not null:
                return new CommandResult { StandardOutput = string.Concat(Managed.TrackedFactoryFiles.Select(path => path + "\n")) };
            default:
                return new CommandResult();
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

        return Program(arguments[(id + 1)..], directory => Path.Combine(_clone.FullName, directory["/work/".Length..]));
    }

    /// <summary>Answers a program with its words, in the container or on the host. <paramref name="results"/> maps the TRX directory to the host.</summary>
    private CommandResult Program(List<string> arguments, Func<string, string> results)
    {
        string program = arguments[0];
        if (program is "sh" && arguments[1] is "-c")
        {
            (int code, string output, string error) = CheckResults.TryGetValue(_builds, out (int Code, string Out, string Err) given) ? given : (0, "", "");
            return new CommandResult { ExitCode = code, StandardOutput = output, StandardError = error };
        }

        if (arguments.Count < 2 || arguments[1] is not "test")
        {
            return new CommandResult();
        }

        Events.Add("dotnet");
        bool red = RedBuilds.Contains(_builds);
        List<(string Class, string Method, string Outcome, string? Message)> tests =
            [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null)];
        if (red)
        {
            tests.Add(("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", RedText));
        }

        if (_builds is 0)
        {
            tests.AddRange(BaseOnlyTests.Select(t => (t.Class, t.Method, "Passed", (string?)null)));
        }

        int at = arguments.IndexOf("--results-directory");
        string directory = results(arguments[at + 1]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "this-run.trx"), Steps.TrxBuilder.Build(tests));
        return new CommandResult { ExitCode = red ? 1 : 0, StandardOutput = TestStdout ?? (red ? RedText : "") };
    }

    private CommandResult Claude(List<string> arguments)
    {
        string text = arguments[arguments.IndexOf("-p") + 1];
        string model = arguments[arguments.IndexOf("--model") + 1];
        string turns = arguments[arguments.IndexOf("--max-turns") + 1];
        string kind = text.StartsWith("Review the build", StringComparison.Ordinal) ? "review" : "build";
        bool error = Turn(kind, model, turns, text);
        return new CommandResult { StandardOutput = Json(error, error ? "build error" : "done") };
    }

    /// <summary>Plays the agent of a session for one message. A preamble may come before the prompt, so the kind is found anywhere in the text.</summary>
    private ManagedTurn SessionMessage(string text, string model)
    {
        string kind = text.Contains("Review the build", StringComparison.Ordinal) ? "review" : "build";
        bool error = Turn(kind, model, "", text);
        return new ManagedTurn(error ? "build error" : "done", error, Pushes: kind is "build" && !NoPushBuilds.Contains(_builds));
    }

    /// <summary>Records one prompt, writes what the agent writes for it and gives the error flag of the answer.</summary>
    private bool Turn(string kind, string model, string turns, string text)
    {
        if (kind is "build")
        {
            _builds++;
        }

        Prompts.Add(new SeenPrompt(kind, model, turns, text, _builds));
        Events.Add(kind);
        if (kind is "build")
        {
            string? report = Report(_builds);
            if (report is not null)
            {
                Write(".factory/threads.json", report);
            }

            if (_builds is 1)
            {
                foreach (string path in FirstBuildWrites)
                {
                    Write(path, "written by the build");
                }

                foreach (string path in FirstBuildAdds)
                {
                    Write(path, "added by the build");
                }

                if (Labels.Contains("refactor"))
                {
                    foreach (string path in FirstBuildChanges.Except(FirstBuildAdds))
                    {
                        File.AppendAllText(Path.Combine(_clone.FullName, path), "\nchanged by the build");
                    }
                }
            }
        }
        else
        {
            Write(".factory/review.json", RejectedBuilds.Contains(_builds)
                ? JsonSerializer.Serialize(new { approved = false, blocking = new[] { Blocking } })
                : """{"approved":true,"blocking":[]}""");
        }

        return kind is "build" && (BuildIsError || ErrorBuilds.Contains(_builds));
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
        string full = Path.Combine(_clone.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private string PullJson() => PullBody is null
        ? JsonSerializer.Serialize(new { head = new { @ref = HeadRef, sha = HeadSha } })
        : JsonSerializer.Serialize(new { body = PullBody, head = new { @ref = HeadRef, sha = HeadSha } });

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

    private sealed class Handler(FeedbackRunHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Uri requestUri = request.RequestUri!;
            if (harness.Managed is { } managed && ManagedRunFake.Handles(requestUri))
            {
                harness.Timeline.Add($"{request.Method.Method} {requestUri.AbsolutePath}");
                (int code, string answer) = managed.Route(request.Method.Method, requestUri, content);
                return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
            }

            harness.Requests.Add(new SeenRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, content));
            string path = request.RequestUri.AbsolutePath;
            (int status, string body) = (200, "{}");
            if (request.Method.Method is "GET")
            {
                if (path.Contains("/issues/", StringComparison.Ordinal))
                {
                    (status, body) = harness.IssueStatus is 200
                        ? (200, JsonSerializer.Serialize(new { title = "Clean up", body = harness.IssueBody, user = new { login = "owner" }, labels = harness.Labels.Select(l => new { name = l }) }))
                        : (harness.IssueStatus, "{\"message\":\"Not Found\"}");
                }
                else
                {
                    (status, body) = harness.HeadStatus is 200
                        ? (200, harness.PullJson())
                        : (harness.HeadStatus, "{\"message\":\"Not Found\"}");
                }
            }
            else if (path.EndsWith("/graphql", StringComparison.Ordinal))
            {
                (status, body) = GraphQl(content ?? "");
            }
            else if (path.EndsWith("/comments", StringComparison.Ordinal))
            {
                harness.Events.Add("comment");
                (status, body) = (201, "{}");
            }

            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private (int Status, string Body) GraphQl(string content)
        {
            using var json = JsonDocument.Parse(content);
            string query = json.RootElement.GetProperty("query").GetString() ?? "";
            JsonElement variables = json.RootElement.GetProperty("variables");
            if (query.Contains("reviewThreads", StringComparison.Ordinal))
            {
                return (200, harness.ThreadsJson());
            }

            string threadId = variables.GetProperty("threadId").GetString() ?? "";
            if (query.Contains("addPullRequestReviewThreadReply", StringComparison.Ordinal))
            {
                harness.Events.Add("reply:" + threadId);
                return threadId == harness.ReplyErrorThread
                    ? (200, "{\"errors\":[{\"message\":\"Could not resolve\"}],\"data\":null}")
                    : (200, "{\"data\":{\"addPullRequestReviewThreadReply\":{\"comment\":{\"id\":\"c\"}}}}");
            }

            harness.Events.Add("resolve:" + threadId);
            return (200, "{\"data\":{\"resolveReviewThread\":{\"thread\":{\"isResolved\":true}}}}");
        }
    }
}
