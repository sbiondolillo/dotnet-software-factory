using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>One prompt the agent got: its kind (plan, build or review), model, turn limit and text.</summary>
public sealed record SeenPrompt(string Kind, string Model, string Turns, string Text, int BuildsBefore);

/// <summary>One request GitHub got.</summary>
public sealed record SeenRequest(string Method, string Uri, string? Content);

/// <summary>A real clone directory, a mock <see cref="CommandRunner"/> that answers git and docker, and a stub GitHub.</summary>
public sealed class FactoryRunHarness : IDisposable
{
    public const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Blocking = "The total ignores the second item of the cart.";
    public const string RedText = "Assert.Equal() Failure: Values differ";

    private readonly DirectoryInfo _clone = Directory.CreateTempSubdirectory("factory-run-clone-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-run-root-");
    private int _builds;

    public FactoryRunHarness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
    }

    public string Owner { get; set; } = "sbiondolillo";

    public string Repo { get; set; } = "factory-scratch";

    public int Issue { get; set; } = 12;

    public string Token { get; set; } = "token";

    public string RunId { get; set; } = "a1b2c3d4";

    public string BaseBranch { get; set; } = "main";

    public List<string> SecretNames { get; set; } = [];

    public string PlanModel { get; set; } = "plan-model";

    public int PlanTurns { get; set; } = 20;

    public string BuildModel { get; set; } = "build-model";

    public int BuildTurns { get; set; } = 60;

    public string ReviewModel { get; set; } = "review-model";

    public int ReviewTurns { get; set; } = 30;

    public string AuthorName { get; set; } = "Factory";

    public string AuthorEmail { get; set; } = "factory@example.com";

    public string Title { get; set; } = "Add a checkout total";

    public string Body { get; set; } = "The checkout shows the total of the cart.";

    public string HeadSha { get; set; } = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";

    public HashSet<string> Lacks { get; } = [];

    public bool AgentWritesPlan { get; set; } = true;

    public bool BuildIsError { get; set; }

    public HashSet<int> RedBuilds { get; } = [];

    public HashSet<int> RejectedBuilds { get; } = [];

    public string? TestStdout { get; set; }

    public int PullNumber { get; set; } = 10;

    public string PullUrl { get; set; } = "https://github.com/sbiondolillo/factory-scratch/pull/10";

    public int PullStatus { get; set; } = 201;

    public bool ChangeAfterBuild { get; set; } = true;

    public bool CloneFails { get; set; }

    public bool TestCommandThrows { get; set; }

    public Action<string>? AfterClone { get; set; }

    public string CloneDirectory => _clone.FullName;

    public List<CommandSpec> Commands { get; } = [];

    public List<SeenPrompt> Prompts { get; } = [];

    public List<SeenRequest> Requests { get; } = [];

    /// <summary>The order in which pushes, comments and container removals happened.</summary>
    public List<string> Events { get; } = [];

    public int Builds => _builds;

    public IEnumerable<SeenRequest> Comments =>
        Requests.Where(r => r.Method is "POST" && r.Uri.EndsWith("/comments", StringComparison.Ordinal));

    public IEnumerable<string> CommentBodies =>
        Comments.Select(c => JsonDocument.Parse(c.Content!).RootElement.GetProperty("body").GetString() ?? "");

    public Task<FactoryRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        var client = new GitHubClient(new HttpClient(new Handler(this)), Token, TimeSpan.Zero);
        var request = new FactoryRunRequest(
            Owner, Repo, Issue, Token, RunId, _clone.FullName, BaseBranch, "factory-sandbox:test", _root.FullName,
            PlanModel, PlanTurns, BuildModel, BuildTurns, ReviewModel, ReviewTurns, AuthorName, AuthorEmail, SecretNames);
        return new FactoryRun(mock.Object, client).RunAsync(request, cancellationToken);
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
            return AnswerGit(spec);
        }

        return spec.Arguments[0] switch
        {
            "run" => new CommandResult { StandardOutput = ContainerId + "\n" },
            "exec" => Exec(spec),
            "rm" => Removed(),
            _ => new CommandResult(),
        };
    }

    private CommandResult Removed()
    {
        Events.Add("rm");
        return new CommandResult();
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
            case "rev-parse":
                return new CommandResult { StandardOutput = HeadSha + "\n" };
            case "status":
                return new CommandResult { StandardOutput = ChangeAfterBuild ? " M src/Checkout.cs\n" : "" };
            case "diff":
                return new CommandResult { StandardOutput = "diff --git a/src/Checkout.cs b/src/Checkout.cs\n" };
            case "push":
                Events.Add("push");
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
        int id = arguments.IndexOf(ContainerId);
        string program = arguments[id + 1];
        if (program is "claude")
        {
            return Claude(arguments);
        }

        string subcommand = arguments[id + 2];
        if (subcommand is not "test")
        {
            return new CommandResult();
        }

        if (TestCommandThrows)
        {
            throw new CommandStartException("dotnet", new InvalidOperationException("cannot start"));
        }

        bool red = RedBuilds.Contains(_builds);
        List<(string Class, string Method, string Outcome, string? Message)> tests =
            [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null)];
        if (red)
        {
            tests.Add(("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", RedText));
        }

        int at = arguments.IndexOf("--results-directory");
        string directory = Path.Combine(_clone.FullName, arguments[at + 1]["/work/".Length..]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "this-run.trx"), Steps.TrxBuilder.Build(tests));
        return new CommandResult { ExitCode = red ? 1 : 0, StandardOutput = TestStdout ?? (red ? RedText : "") };
    }

    private CommandResult Claude(List<string> arguments)
    {
        string text = arguments[arguments.IndexOf("-p") + 1];
        string model = arguments[arguments.IndexOf("--model") + 1];
        string turns = arguments[arguments.IndexOf("--max-turns") + 1];
        string kind = text.StartsWith("Plan the work", StringComparison.Ordinal) ? "plan"
            : text.StartsWith("Review the build", StringComparison.Ordinal) ? "review" : "build";
        if (kind is "build")
        {
            _builds++;
        }

        Prompts.Add(new SeenPrompt(kind, model, turns, text, _builds));
        if (kind is "plan" && AgentWritesPlan)
        {
            Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
            Write(".factory/plan.md", PlanPhaseHarness.PlanText);
        }

        if (kind is "review")
        {
            bool rejected = RejectedBuilds.Contains(_builds);
            Write(".factory/review.json", rejected
                ? JsonSerializer.Serialize(new { approved = false, blocking = new[] { Blocking } })
                : """{"approved":true,"blocking":[]}""");
        }

        return new CommandResult { StandardOutput = Json(kind is "build" && BuildIsError, kind is "build" && BuildIsError ? "build error" : "done") };
    }

    private void Write(string relative, string text)
    {
        string full = Path.Combine(_clone.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private sealed class Handler(FactoryRunHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string uri = request.RequestUri!.AbsoluteUri;
            harness.Requests.Add(new SeenRequest(request.Method.Method, uri, content));
            string path = request.RequestUri.AbsolutePath;
            (int status, string body) = (request.Method.Method, path) switch
            {
                ("GET", _) when path.EndsWith("/pulls", StringComparison.Ordinal) => (200, "[]"),
                ("GET", _) => (200, JsonSerializer.Serialize(new
                {
                    title = harness.Title,
                    body = harness.Body,
                    user = new { login = "ada" },
                    labels = Array.Empty<object>(),
                })),
                ("POST", _) when path.EndsWith("/pulls", StringComparison.Ordinal) =>
                    (harness.PullStatus, harness.PullStatus is 201
                        ? JsonSerializer.Serialize(new { number = harness.PullNumber, html_url = harness.PullUrl })
                        : "{\"message\":\"Validation Failed\"}"),
                _ => (201, "{}"),
            };
            if (path.EndsWith("/comments", StringComparison.Ordinal) && request.Method.Method is "POST")
            {
                harness.Events.Add("comment");
            }

            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
