using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>What one answer of <c>claude</c> holds: its turns, its cost as written, and the seconds it takes.</summary>
public sealed record SummaryAnswer(int Turns, string Cost, double Seconds, bool IsError = false, string Text = "done");

/// <summary>A clock that moves only when a stub advances it, in milliseconds.</summary>
public sealed class StepClock : TimeProvider
{
    private long _milliseconds;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _milliseconds;

    public void Advance(double seconds) => _milliseconds += (long)(seconds * 1000);
}

/// <summary>A real clone directory, a mock command runner for git, docker and claude, a stub GitHub and a fake clock, for the job summary.</summary>
public sealed class JobSummaryHarness : IDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Check = "grep -q Install README.md";

    private readonly DirectoryInfo _clone = Directory.CreateTempSubdirectory("job-summary-clone-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("job-summary-root-");
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("job-summary-file-");
    private readonly StepClock _clock = new();
    private int _builds;
    private int _reviews;
    private int _plans;

    public JobSummaryHarness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        SummaryPath = Path.Combine(_files.FullName, "summary.md");
        File.WriteAllText(SummaryPath, "");
    }

    /// <summary>The path the run gets as the summary file, or null for none.</summary>
    public string? SummaryPath { get; set; }

    public List<string> Labels { get; } = [];

    public string Body { get; set; } = "The checkout shows the total of the cart.";

    public List<SummaryAnswer> PlanAnswers { get; } = [];

    public Dictionary<int, SummaryAnswer> BuildAnswers { get; } = [];

    public Dictionary<int, SummaryAnswer> ReviewAnswers { get; } = [];

    /// <summary>True when the first plan prompt writes no plan, so its gate fails and a second prompt follows.</summary>
    public bool FirstPlanWritesNothing { get; set; }

    public Dictionary<int, double> LintSeconds { get; } = [];

    public Dictionary<int, double> TestSeconds { get; } = [];

    public Dictionary<int, double> CheckSeconds { get; } = [];

    /// <summary>The builds whose test gate is red, with the two Redo texts the gate gives.</summary>
    public Dictionary<int, (string First, string Second)> RedBuilds { get; } = [];

    /// <summary>The exit code of the check after the build of that number; a number with no entry is 0 after a build and 1 before the first.</summary>
    public Dictionary<int, int> CheckCodes { get; } = [];

    public Dictionary<int, string> BlockedBuilds { get; } = [];

    public bool LintFails { get; set; }

    public StringWriter Progress { get; } = new();

    public StringWriter Errors { get; } = new();

    public List<CommandSpec> Commands { get; } = [];

    public int Comments { get; private set; }

    public string Summary => File.Exists(SummaryPath ?? "") ? File.ReadAllText(SummaryPath!).Replace("\r\n", "\n", StringComparison.Ordinal) : "";

    public string FilesDirectory => _files.FullName;

    public Task<FactoryRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        var client = new GitHubClient(new HttpClient(new Handler(this)), "token", TimeSpan.Zero);
        var request = new FactoryRunRequest(
            "sbiondolillo", "factory-scratch", 12, "token", "a1b2c3d4", _clone.FullName, "main", "factory-sandbox:test", _root.FullName,
            "plan-model", 20, "build-model", 60, "review-model", 30, "Factory", "factory@example.com", []);
        return new FactoryRun(mock.Object, client, Progress, Errors, _clock, SummaryPath).RunAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        _clone.Delete(recursive: true);
        _root.Delete(recursive: true);
        _files.Delete(recursive: true);
    }

    private CommandResult Answer(CommandSpec spec)
    {
        Commands.Add(spec);
        if (spec.Command is "git")
        {
            return spec.Arguments[0] switch
            {
                "clone" => Cloned(spec.Arguments[2]),
                "rev-parse" => new CommandResult { StandardOutput = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c\n" },
                "status" => new CommandResult { StandardOutput = " M src/Checkout.cs\n" },
                "diff" => new CommandResult { StandardOutput = spec.Arguments.Contains("--name-only") ? "" : "diff --git a/src/Checkout.cs b/src/Checkout.cs\n" },
                _ => new CommandResult(),
            };
        }

        return spec.Arguments[0] switch
        {
            "run" => new CommandResult { StandardOutput = ContainerId + "\n" },
            "exec" => Exec(spec),
            _ => new CommandResult(),
        };
    }

    private static CommandResult Cloned(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "Solution.slnx"), "<Solution />");
        foreach (string path in new[] { "specs/features/", "tests/Specs/", "tests/Integration/" })
        {
            Directory.CreateDirectory(Path.Combine(directory, path));
        }

        return new CommandResult();
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

        string subcommand = arguments.Count > id + 2 ? arguments[id + 2] : "";
        if (program is "sh" && subcommand is "-c")
        {
            _clock.Advance(_builds is 0 ? 0 : CheckSeconds.GetValueOrDefault(_builds));
            int code = CheckCodes.TryGetValue(_builds, out int given) ? given : _builds is 0 ? 1 : 0;
            return new CommandResult { ExitCode = code };
        }

        if (program is "dotnet" && subcommand is "format" && !arguments.Contains("--verify-no-changes"))
        {
            _clock.Advance(LintSeconds.GetValueOrDefault(_builds));
            return LintFails ? new CommandResult { ExitCode = 1, StandardError = "format failed" } : new CommandResult();
        }

        return program is "dotnet" && subcommand is "test" ? Test(arguments) : new CommandResult();
    }

    private CommandResult Test(List<string> arguments)
    {
        bool red = RedBuilds.TryGetValue(_builds, out (string First, string Second) texts);
        if (_builds > 0)
        {
            _clock.Advance(TestSeconds.GetValueOrDefault(_builds));
        }

        List<(string Class, string Method, string Outcome, string? Message)> tests =
            [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null)];
        string stdout = "";
        if (red)
        {
            const string Lead = "failed test: ";
            if (texts.Second.StartsWith(Lead, StringComparison.Ordinal))
            {
                string[] parts = texts.Second[Lead.Length..].Split(": ", 2);
                int dot = parts[0].LastIndexOf('.');
                tests.Add((parts[0][..dot], parts[0][(dot + 1)..], "Failed", parts[1]));
            }
            else
            {
                stdout = texts.Second;
            }
        }

        string results = Path.Combine(_clone.FullName, arguments[arguments.IndexOf("--results-directory") + 1]["/work/".Length..]);
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "this-run.trx"), Steps.TrxBuilder.Build(tests));
        return new CommandResult { ExitCode = red ? 1 : 0, StandardOutput = stdout };
    }

    private CommandResult Claude(List<string> arguments)
    {
        string text = arguments[arguments.IndexOf("-p") + 1];
        SummaryAnswer answer;
        if (text.StartsWith("Plan the work", StringComparison.Ordinal))
        {
            _plans++;
            answer = _plans <= PlanAnswers.Count ? PlanAnswers[_plans - 1] : new SummaryAnswer(1, "0", 0);
            bool writes = !(FirstPlanWritesNothing && _plans is 1) && !answer.IsError;
            if (writes)
            {
                if (!Labels.Contains("refactor"))
                {
                    Write("specs/features/checkout.feature", PlanPhaseHarness.SoundFeature);
                }

                Write(".factory/plan.md", PlanPhaseHarness.PlanText);
            }
        }
        else if (text.StartsWith("Review the build", StringComparison.Ordinal))
        {
            _reviews++;
            answer = ReviewAnswers.GetValueOrDefault(_reviews) ?? new SummaryAnswer(1, "0", 0);
            if (!answer.IsError)
            {
                Write(
                    ".factory/review.json",
                    BlockedBuilds.TryGetValue(_builds, out string? item)
                        ? JsonSerializer.Serialize(new { approved = false, blocking = new[] { item } })
                        : """{"approved":true,"blocking":[]}""");
            }
        }
        else
        {
            _builds++;
            answer = BuildAnswers.GetValueOrDefault(_builds) ?? new SummaryAnswer(1, "0", 0);
        }

        _clock.Advance(answer.Seconds);
        string json = $"{{\"is_error\":{(answer.IsError ? "true" : "false")},\"result\":{JsonSerializer.Serialize(answer.Text)},\"num_turns\":{answer.Turns},\"total_cost_usd\":{answer.Cost}}}";
        return new CommandResult { StandardOutput = json };
    }

    private void Write(string relative, string text)
    {
        string full = Path.Combine(_clone.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>The body of an issue with the scope and the check of a cleanup run.</summary>
    public static string CleanupBody => $"## Scope\nREADME.md\n\n## Check\n\n```sh\n{Check}\n```\n";

    private sealed class Handler(JobSummaryHarness harness) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string method = request.Method.Method;
            if (method is "POST" && path.EndsWith("/comments", StringComparison.Ordinal))
            {
                harness.Comments++;
            }

            (int status, string body) = (method, path) switch
            {
                ("GET", _) when path.EndsWith("/pulls", StringComparison.Ordinal) => (200, "[]"),
                ("GET", _) => (200, JsonSerializer.Serialize(new
                {
                    title = "Add a checkout total",
                    body = harness.Body,
                    user = new { login = "ada" },
                    labels = harness.Labels.Select(name => new { name }).ToArray(),
                })),
                ("POST", _) when path.EndsWith("/pulls", StringComparison.Ordinal) =>
                    (201, JsonSerializer.Serialize(new { number = 10, html_url = "https://github.com/sbiondolillo/factory-scratch/pull/10" })),
                _ => (201, "{}"),
            };
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
